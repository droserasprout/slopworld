import { dotnet } from './_framework/dotnet.js';
import { verifyInput } from './input.js';
import { workspaceViewport, workspaceParameters } from './workspace-viewport.js';
let workspace = null;
let entry = null, gameSession = null, gameDraws = null, savedGame = null, terrain = null;
const parameters = workspaceParameters();
const app = parameters.has('workspace') && parameters.has('app');
if (app) {
    document.title = 'SlopWorld';
    document.body.classList.add('workspace-app');
    document.querySelector('h1').textContent = 'SlopWorld';
    document.getElementById('result').textContent = 'Loading the workspace…';
}
try {
    if (parameters.has('workspace') && ['play', 'load', 'save'].some(key => parameters.has(key)))
        throw new Error('The Eco workspace cannot create or load a colony');
    // Managed debugging disables Mono's Jiterpreter. Keep it opt-in for workspace
    // runs; the diagnostic probes retain their existing debugger configuration.
    const runtime = await (parameters.has('workspace') && !parameters.has('managedDebug')
        ? dotnet.withDebugging(0) : dotnet).create();
    const exports = await runtime.getAssemblyExports('Probe.dll');
    window.__loading?.status('Loading game assemblies...');
    const manifest = await fetch('./game/manifest.json').then(r => r.json());
    const loaded = [];
    const loadErrors = [];
    for (const item of manifest) {
        try {
            const bypassLogger = parameters.has('skipLogger') && item.file === 'RimWorld.dll';
            const response = await fetch(`./game/${bypassLogger ? 'skip-logger/' : ''}${item.file}`);
            if (!response.ok) throw new Error(`${item.file}: HTTP ${response.status}`);
            loaded.push(exports.Probe.LoadAssembly(new Uint8Array(await response.arrayBuffer())));
        } catch (error) {
            loadErrors.push({ file: item.file, error: String(error) });
        }
    }
    const missingDependencies = await fetch('./game/missing.json').then(r => r.json());
    const adaptations = await fetch('./game/adaptations.json').then(r => r.json());
    if (parameters.has('skipLogger')) adaptations.push('InitLogger body replaced with ret');
    window.__managedProbe = exports.Probe;
    let offlineManifest = null;
    if (parameters.has('patched')) {
        const response = await fetch('./mod/SlopWorld.dll');
        if (!response.ok) throw new Error(`SlopWorld.dll: HTTP ${response.status}`);
        loaded.push(exports.Probe.LoadAssembly(new Uint8Array(await response.arrayBuffer())));
        offlineManifest = await fetch('./mod/offline.json').then(r => r.json());
    }
    let gui = null;
    if (parameters.has('gui')) {
        const response = await fetch('./Adapters.dll');
        if (!response.ok) throw new Error('GUI adapter missing');
        exports.Probe.LoadAssembly(new Uint8Array(await response.arrayBuffer()));
        const commands = JSON.parse(exports.Probe.GuiControlProbe());
        gui = (await import('./gui.js')).renderGuiProbe(commands);
        gui.input = await (await import('./gui-input.js')).verifyGuiInput(exports.Probe);
        gui.targets = (await import('./gui-targets.js')).verifyGuiTargets(exports.Probe);
    }
    let modHttp = null, modSocket = null, modHub = null;
    if (parameters.has('modHttp')) {
        for (const file of ['Adapters.dll', 'mod/Google.Protobuf.dll', 'mod/SlopWorld.dll']) {
            const response = await fetch('./' + file);
            if (!response.ok) throw new Error('Missing ' + file);
            exports.Probe.LoadAssembly(new Uint8Array(await response.arrayBuffer()));
        }
        modHttp = JSON.parse(await exports.Probe.ModHttp(location.origin));
        modSocket = JSON.parse(await exports.Probe.ModSocket(location.origin));
        modHub = JSON.parse(await exports.Probe.ModHub(location.origin));

    }
    let eco = null;
    async function initializeEco() {
        const response = await fetch('./mod/SlopWorld.dll');
        if (!response.ok) throw new Error('Eco mod missing');
        exports.Probe.LoadAssembly(new Uint8Array(await response.arrayBuffer()));
        eco = JSON.parse(exports.Probe.InitializeEco());
    }
    if (parameters.has('eco') && !parameters.has('entry')) await initializeEco();
    const offline = parameters.has('offline') ? JSON.parse(exports.Probe.OfflineHook()) : null;
    let assetTransfer = null;
    if (parameters.has('assets')) {
        const response = await fetch('./assets/Assets.zip');
        if (!response.ok) throw new Error(`Assets.zip: HTTP ${response.status}`);
        exports.Probe.BeginAssetUpload();
        let bytes = 0, committed = false;
        try {
            for await (const chunk of response.body) {
                exports.Probe.AssetChunk(chunk);
                bytes += chunk.length;
            }
            const expected = Number(response.headers.get('content-length'));
            if (!expected || bytes !== expected) throw new Error(`Incomplete asset transfer: ${bytes}/${expected}`);
            exports.Probe.EndAssetUpload(true);
            committed = true;
            assetTransfer = { bytes, destination: '/Assets.zip', persistent: false };
        } finally {
            if (!committed) exports.Probe.EndAssetUpload(false);
        }
    }
    const draws = parameters.has('draws') ? await (await import('./draws.js')).renderSubmissions(exports.Probe) : null;
    if (parameters.has('entry')) {
        const adapters = await fetch('./Adapters.dll');
        if (!adapters.ok) throw new Error(`Adapters.dll: HTTP ${adapters.status}`);
        loaded.push(exports.Probe.LoadAssembly(new Uint8Array(await adapters.arrayBuffer())));
        const response = await fetch('./assets/Core.zip');
        if (!response.ok) throw new Error(`Core.zip: HTTP ${response.status}`);
        const files = exports.Probe.InstallCore(new Uint8Array(await response.arrayBuffer()));
        try {
            entry = JSON.parse(exports.Probe.StartEntry());
            if (entry.checks.every(c => c.ok)) for (let frame = 0; frame < 5; frame++) {
                await new Promise(requestAnimationFrame);
                entry = JSON.parse(exports.Probe.EntryFrame(frame));
            }
            entry.files = files;
            if (entry.loaded && parameters.has('workspace')) {
                const viewport = workspaceViewport(parameters);
                exports.Probe.ConfigureWorkspaceViewport(viewport.width, viewport.height, viewport.uiScale);
                document.documentElement.style.setProperty('--workspace-aspect', viewport.width / viewport.height);
                const content = await fetch('./mod/Content.zip');
                if (!content.ok) throw new Error('Workspace mod content is missing');
                const textures = exports.Probe.InstallModContent(new Uint8Array(await content.arrayBuffer()));
                console.log('Browser: mounted ' + textures + ' mod textures');
                await initializeEco();
                if (parameters.has('referenceAppearance')) {
                    const appearance = await fetch('./assets/reference-appearance.json');
                    if (!appearance.ok) throw new Error('Reference appearance is missing');
                    exports.Probe.WorkspaceAppearance(await appearance.text());
                }
                if (app && parameters.get('localPreferences') !== 'off') {
                    (await import('./workspace-preferences.js')).installWorkspacePreferences(exports.Probe);
                    (await import('./workspace-keybindings.js')).installWorkspaceKeyBindings(exports.Probe);
                }
                if (parameters.has('nativeFonts')) {
                    const chains = await fetch('./assets/fonts/chains.json');
                    if (!chains.ok) throw new Error('Host font family order is missing');
                    exports.Probe.WorkspaceFontChains(await chains.text());
                    const manifest = await fetch('./assets/fonts/manifest.json');
                    if (!manifest.ok) throw new Error('Local native font bakes are missing');
                    // Styles and densities share font bytes. Retain them only for
                    // this installation pass, avoiding repeated fetch/body copies.
                    const fonts = new Map();
                    const loadFont = async url => {
                        const response = await fetch(url);
                        if (!response.ok) throw new Error('Local native font bake is incomplete');
                        return new Uint8Array(await response.arrayBuffer());
                    };
                    for (const face of await manifest.json()) {
                        const base = new URL('./assets/fonts/', location.href);
                        const fontUrl = new URL(face.font, base), bakeUrl = new URL(face.bake, base);
                        if (!fontUrl.href.startsWith(base.href) || !bakeUrl.href.startsWith(base.href))
                            throw new Error('Font manifest path escapes local font directory');
                        if (!fonts.has(fontUrl.href)) fonts.set(fontUrl.href, loadFont(fontUrl));
                        const [font, bake] = await Promise.all([fonts.get(fontUrl.href), fetch(bakeUrl)]);
                        if (!bake.ok) throw new Error('Local native font bake is incomplete');
                        exports.Probe.WorkspaceFont(font, await bake.text(), face.index ?? -1);
                    }
                }
                    for (const name of ['0Harmony', 'Google.Protobuf', 'Markdig', 'AngleSharp', 'Tomlyn']) {
                        const response = await fetch(`./mod/${name}.dll`);
                        if (!response.ok) throw new Error('Missing workspace dependency ' + name);
                        exports.Probe.LoadAssembly(new Uint8Array(await response.arrayBuffer()));
                    }
                    // Session rows use the shared worker icon without snapshot assets.
                    const portraits = 0;
                    let commands = JSON.parse(await exports.Probe.WorkspaceFrame(location.origin, parameters.get('session') ?? ''));
                    const drawing = await import('./gui.js');
                    const canvas = document.createElement('canvas'); canvas.width = viewport.width; canvas.height = viewport.height;
                    // Select before the first context exists. Keep experimental until
                    // whole-workspace pixels, memory and replay costs are measured.
                    if(parameters.has('floatCanvas')) {
                        const context=canvas.getContext('2d',{colorType:'float16'});
                        if(context.getContextAttributes().colorType!=='float16')
                            throw new Error('This browser does not support float16 Canvas');
                    }
                    document.body.prepend(canvas);
                    drawing.renderGui(canvas, commands);
                    console.log('Browser: workspace opened');
                    for (let frame = 0; frame < 30; frame++) {
                        await new Promise(requestAnimationFrame);
                        commands = JSON.parse(exports.Probe.WorkspaceUpdate('[]', frame / 60)).commands;
                        drawing.renderGui(canvas, commands);
                        if (frame % 10 === 9) console.log('Browser: workspace frame ' + (frame + 1));
                    }
                    workspace = drawing.renderGuiProbe(commands, false, canvas);
                    workspace.frames = 30;
                    workspace.portraits = portraits;
                    workspace.fonts = JSON.parse(exports.Probe.WorkspaceFontSnapshot());
                    workspace.textures = textures;
                    workspace.game = JSON.parse(exports.Probe.GameSnapshot());
                    workspace.state = JSON.parse(exports.Probe.WorkspaceState());
                    if (parameters.has('live')) workspace.live = (await import('./workspace.js')).startWorkspace(exports.Probe, canvas, 30 / 60);
                    if (app) {
                        document.body.classList.add('ready');
                        window.__loading?.finish();
                    }
                }

            if (parameters.has('play') && entry.loaded) {
                exports.Probe.ConfigureGameViewport(1280, 720);
                if (parameters.has('load')) {
                    const save = await fetch('./assets/BrowserColony.rws');
                    if (!save.ok) throw new Error(`BrowserColony.rws: HTTP ${save.status}`);
                    exports.Probe.LoadGame(new Uint8Array(await save.arrayBuffer()));
                } else entry = JSON.parse(exports.Probe.RequestPlay());
                gameSession = [];
                let resumed = false;
                for (let frame = 5; frame < 15; frame++) {
                    await new Promise(requestAnimationFrame);
                    entry = JSON.parse(exports.Probe.EntryFrame(frame));
                    const snapshot = JSON.parse(exports.Probe.GameSnapshot());
                    gameSession.push(snapshot);
                    if (!resumed && snapshot.programState === 'Playing' && snapshot.maps?.length > 0) {
                        if (parameters.has('eco')) await initializeEco();
                        exports.Probe.ResumePlay();
                        resumed = true;
                    }
                }
                if (resumed) for (let frame = 15; frame < 315; frame++) {
                    await new Promise(requestAnimationFrame);
                    entry = JSON.parse(exports.Probe.EntryFrame(frame));
                    if (frame % 60 === 14) gameSession.push(JSON.parse(exports.Probe.GameSnapshot()));
                }
                gameDraws = JSON.parse(exports.Probe.DrawInventory());
                if (resumed && parameters.has('terrain')) {
                    const objects = parameters.has('objects');
                    const frame = JSON.parse(objects ? exports.Probe.CaptureObjects() : exports.Probe.CaptureTerrain());
                    const canvas = document.createElement('canvas');
                    canvas.width = frame.camera.width;
                    canvas.height = frame.camera.height;
                    document.getElementById('result').before(canvas);
                    const { createTerrainRenderer } = await import('./graphics/terrain-renderer.js');
                    const renderer = await createTerrainRenderer(canvas, new URL('./graphics/', location.href), objects);
                    try {
                        terrain = { ...renderer.draw(frame), omitted: frame.omitted, persistent: frame.persistent,
                            camera: frame.camera, source: 'installed game draw snapshot', mipLevels: 1 };
                    } finally { renderer.dispose(); }
                }
                if (resumed && parameters.has('save')) {
                    const bytes = exports.Probe.SaveGame();
                    const chunks = [];
                    for (let i = 0; i < bytes.length; i += 16384)
                        chunks.push(String.fromCharCode(...bytes.subarray(i, i + 16384)));
                    window.__gameSaveBase64 = btoa(chunks.join(''));
                    savedGame = { bytes: bytes.length, name: 'BrowserColony.rws' };
                }
            }
        } finally { if (!parameters.has('live')) exports.Probe.StopEntry(); }
    }
    const jobs = parameters.has('jobs') ? JSON.parse(exports.Probe.Jobs()) : null;
    const engineScene = parameters.has('scene') ? await (await import('./scene.js')).runScene(exports.Probe) : null;
    const lifecycle = parameters.has('lifecycle') ? JSON.parse(exports.Probe.BootstrapHeadless()) : null;
    const input = parameters.has('input') ? verifyInput(exports.Probe) : null;
    const deep = parameters.has('deep') ? JSON.parse(exports.Probe.Deep()) : null;
    // The baseline probe invokes the executable entry point and resets global engine
    // state. Never run it after opening the live workspace.
    const baseline = parameters.has('workspace') ? {} : JSON.parse(exports.Probe.Run());
    window.__probeResult = { ...baseline, loaded, loadErrors, manifest, missingDependencies,
        deep, gui, eco, modHttp, modSocket, modHub, workspace,
        input,
        lifecycle,
        jobs,
        engineScene,
        entry,
        gameSession,
        gameDraws,
        savedGame,
        terrain,
        offline, offlineManifest,
        draws,
        assetTransfer,
        adaptations,
        scene: parameters.has('workspace') ? null : JSON.parse(exports.Probe.SceneGeometry()) };
} catch (error) {
    window.__probeResult = { error: String(error), stack: error.stack, entry, gameSession, gameDraws, savedGame, terrain };
}
document.getElementById('result').textContent = JSON.stringify(window.__probeResult, null, 2);
if (app && window.__probeResult.error) {
    window.__loading?.finish();
    document.body.classList.remove('ready');
    document.getElementById('result').textContent = 'Workspace could not start.\n' + window.__probeResult.error;
}
