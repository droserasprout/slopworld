import { dotnet } from './_framework/dotnet.js';
import { workspaceViewport, workspaceParameters } from './workspace-viewport.js';
let workspace = null;
let entry = null;
const parameters = workspaceParameters();
document.title = 'SlopWorld';
document.body.classList.add('workspace-app');
document.querySelector('h1').textContent = 'SlopWorld';
document.getElementById('result').textContent = 'Loading the workspace…';
try {
    if (['play', 'load', 'save'].some(key => parameters.has(key)))
        throw new Error('The Eco workspace cannot create or load a colony');
    // Managed debugging disables Mono's Jiterpreter; keep it opt-in.
    const runtime = await (!parameters.has('managedDebug')
        ? dotnet.withDebugging(0) : dotnet).create();
    const exports = await runtime.getAssemblyExports('Probe.dll');
    window.__loading?.status('Loading game assemblies...');
    const manifest = await fetch('./game/manifest.json').then(r => r.json());
    const loaded = [];
    const loadErrors = [];
    for (const item of manifest) {
        try {
            const response = await fetch(`./game/${item.file}`);
            if (!response.ok) throw new Error(`${item.file}: HTTP ${response.status}`);
            loaded.push(exports.Probe.LoadAssembly(new Uint8Array(await response.arrayBuffer())));
        } catch (error) {
            loadErrors.push({ file: item.file, error: String(error) });
        }
    }
    const missingDependencies = await fetch('./game/missing.json').then(r => r.json());
    const adaptations = await fetch('./game/adaptations.json').then(r => r.json());
    window.__managedProbe = exports.Probe;
    let eco = null;
    async function initializeEco() {
        const response = await fetch('./mod/SlopWorld.dll');
        if (!response.ok) throw new Error('Eco mod missing');
        exports.Probe.LoadAssembly(new Uint8Array(await response.arrayBuffer()));
        eco = JSON.parse(exports.Probe.InitializeEco());
    }
    let assetTransfer = null;
    {
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

    {
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
            if (entry.loaded) {
                const viewport = workspaceViewport(parameters);
                exports.Probe.ConfigureWorkspaceViewport(viewport.width, viewport.height, viewport.uiScale);
                document.documentElement.style.setProperty('--workspace-aspect', viewport.width / viewport.height);
                const content = await fetch('./mod/Content.zip');
                if (!content.ok) throw new Error('Workspace mod content is missing');
                const textures = exports.Probe.InstallModContent(new Uint8Array(await content.arrayBuffer()));
                console.log('Browser: mounted ' + textures + ' mod textures');
                await initializeEco();
                let fontDefaults = null, fontChains = null;
                if (parameters.has('referenceAppearance')) {
                    const appearance = await fetch('./assets/reference-appearance.json');
                    if (!appearance.ok) throw new Error('Reference appearance is missing');
                    fontDefaults = await appearance.text();
                    exports.Probe.WorkspaceAppearance(fontDefaults);
                }
                if (parameters.get('localPreferences') !== 'off') {
                    (await import('./workspace-preferences.js')).installWorkspacePreferences(exports.Probe);
                    (await import('./workspace-keybindings.js')).installWorkspaceKeyBindings(exports.Probe);
                }
                if (parameters.has('nativeFonts')) {
                    await (await import('./workspace-rasterizer.js')).installLocalRasterizer(exports.Probe);
                    const catalog = await fetch('./assets/fonts/catalog.json');
                    if (catalog.ok) exports.Probe.WorkspaceFontCatalog(await catalog.text());
                    const chains = await fetch('./assets/fonts/chains.json');
                    if (!chains.ok) throw new Error('Host font family order is missing');
                    fontChains = await chains.text();
                    exports.Probe.WorkspaceFontChains(fontChains);
                    const manifest = await fetch('./assets/fonts/manifest.json');
                    if (!manifest.ok) throw new Error('Host font descriptors are missing');
                    // Styles and densities share font bytes. Retain them only for
                    // this installation pass, avoiding repeated fetch/body copies.
                    const fonts = new Map(), families = new Set();
                    const loadFont = async url => {
                        const response = await fetch(url);
                        if (!response.ok) throw new Error('Host font file is unavailable');
                        return new Uint8Array(await response.arrayBuffer());
                    };
                    for (const face of await manifest.json()) {
                        const base = new URL('./assets/fonts/', location.href);
                        const fontUrl = new URL(face.font, base), bakeUrl = new URL(face.bake, base);
                        if (!fontUrl.href.startsWith(base.href) || !bakeUrl.href.startsWith(base.href))
                            throw new Error('Font manifest path escapes local font directory');
                        if (!fonts.has(fontUrl.href)) fonts.set(fontUrl.href, face.hash
                            ? (await import('./host-fonts.js')).hostFonts(exports.Probe).file(face.hash)
                            : loadFont(fontUrl));
                        const [font, bake] = await Promise.all([fonts.get(fontUrl.href), fetch(bakeUrl)]);
                        if (!bake.ok) throw new Error('Host font descriptor is unavailable');
                        const descriptor=await bake.text();
                        exports.Probe.WorkspaceFont(font, descriptor, face.index ?? -1);
                        const family=JSON.parse(descriptor).family;
                        if (family) families.add(family);
                    }
                    await (await import('./font-delivery.js')).warmWorkspaceFonts(exports.Probe,[...families]);
                }
                for (const name of ['0Harmony', 'Google.Protobuf', 'Markdig', 'AngleSharp', 'Tomlyn']) {
                    const response = await fetch(`./mod/${name}.dll`);
                    if (!response.ok) throw new Error('Missing workspace dependency ' + name);
                    exports.Probe.LoadAssembly(new Uint8Array(await response.arrayBuffer()));
                }
                // Session rows use the shared worker icon without snapshot assets.
                const portraits = 0;
                if (parameters.has('nativeFonts')) {
                    const fontLoader = await import('./workspace-fonts.js');
                    const fonts = fontLoader.dynamicFonts(exports.Probe, globalThis.fetch, () => workspaceViewport(parameters).uiScale, undefined, fontDefaults ? {selection:fontDefaults, chains:fontChains} : null);
                    void fonts(exports.Probe.WorkspaceDynamicFonts(fonts));
                }
                let commands = JSON.parse(await exports.Probe.WorkspaceFrame(location.origin, parameters.get('session') ?? ''));
                if (parameters.has('nativeFonts') && await (await import('./font-delivery.js')).warmWorkspaceFonts(exports.Probe))
                    commands = JSON.parse(exports.Probe.WorkspaceUpdate('[]', 0)).commands;
                const drawing = await import('./gui.js');
                const canvas = document.createElement('canvas'); canvas.width = viewport.width; canvas.height = viewport.height;
                document.body.prepend(canvas);
                drawing.renderGui(canvas, commands);
                console.log('Browser: workspace opened');
                for (let frame = 0; frame < 30; frame++) {
                    await new Promise(requestAnimationFrame);
                    commands = JSON.parse(exports.Probe.WorkspaceUpdate('[]', frame / 60)).commands;
                    if (parameters.has('nativeFonts') && await (await import('./font-delivery.js')).warmWorkspaceFonts(exports.Probe))
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
                workspace.live = (await import('./workspace.js')).startWorkspace(exports.Probe, canvas, 30 / 60);
                document.body.classList.add('ready');
                window.__loading?.finish();
            }

        } catch (error) { exports.Probe.StopEntry(); throw error; }
    }
    window.__probeResult = { loaded, loadErrors, manifest, missingDependencies,
        workspace, entry, eco, assetTransfer, adaptations };
} catch (error) {
    window.__probeResult = { error: String(error), stack: error.stack, entry };
}
document.getElementById('result').textContent = JSON.stringify(window.__probeResult, null, 2);
if (window.__probeResult.error) {
    window.__loading?.finish();
    document.body.classList.remove('ready');
    document.getElementById('result').textContent = 'Workspace could not start.\n' + window.__probeResult.error;
}
