import { publishedAssemblies } from './assembly-mode.js';
import { nativeWasmResource } from './native-wasm.js';
import { workspaceViewport, workspaceParameters } from './workspace-viewport.js';
import { loadStartupAssembly, loadStartupAssemblies } from './startup-assemblies.js';
import { yieldStartupPaint } from './startup-yield.js';
import { jukeboxLikes } from './jukebox-likes.js';
import { installCoreLanguage } from './core-language.js';
function entryReport(probe) {
    try { return JSON.parse(probe.EntryReport()); }
    catch (error) { return {diagnosticError: String(error)}; }
}

export async function startRuntime(dotnet, parameters = workspaceParameters(), {
    window = globalThis.window, document = window.document, fetch = globalThis.fetch,
    published = publishedAssemblies, paint = yieldStartupPaint, loadModule = path => import(path),
} = {}) {
    let stage = 'Starting the .NET runtime';
    let workspace = null;
    let entry = null;
    document.title = 'SlopWorld';
    document.body.classList.add('workspace-app');
    document.querySelector('h1').textContent = 'SlopWorld';
    document.getElementById('result').textContent = 'Loading the workspace…';
    try {
        if (['play', 'load', 'save'].some(key => parameters.has(key)))
            throw new Error('The Eco workspace cannot create or load a colony');
        const builder = dotnet.withResourceLoader(nativeWasmResource);
        // Managed debugging disables Mono's Jiterpreter; keep it opt-in.
        if (!parameters.has('managedDebug')) builder.withDebugging(0);
        const runtime = await builder.create();
        const exports = await runtime.getAssemblyExports('Probe.dll');
        const assemblyRequest = published ? null : fetch;
        const loadAssembly = published
            ? (_, file) => exports.Probe.LoadPublishedAssembly(file.replace(/\.dll$/, ''))
            : bytes => exports.Probe.LoadAssembly(bytes);
        stage = 'Loading game assemblies';
        window.__loading?.status('Loading game assemblies...');
        const manifest = await fetch('./game/manifest.json').then(r => r.json());
        const {loaded, loadErrors} = await loadStartupAssemblies(manifest, loadAssembly, assemblyRequest, paint);
        if (loadErrors.length) {
            const details = loadErrors.map(({file, error}) => `${file}: ${error}`).join('\n');
            throw new Error('Failed to load game assemblies:\n' + details);
        }
        const [missingDependencies, adaptations] = await Promise.all([
            fetch('./game/missing.json').then(r => r.json()),
            fetch('./game/adaptations.json').then(r => r.json())
        ]);
        window.__managedProbe = exports.Probe;
        const loadFile = path => loadStartupAssembly(path, loadAssembly, assemblyRequest, paint);
        let eco = null;
        let assetTransfer = null;
        {
            stage = 'Streaming assets/Eco.zip';
            const archive = await (await loadModule('./asset-archive.js')).openAssetArchive('./assets/Eco.zip');
            const response = archive.response;
            if (!response.ok) throw new Error(`Eco.zip: HTTP ${response.status}`);
            if (!response.body) throw new Error('The archive response has no body');
            exports.Probe.BeginAssetUpload();
            let bytes = 0, committed = false;
            let reader;
            try {
                reader = response.body.getReader();
                for (;;) {
                    const {value: chunk, done} = await reader.read();
                    if (done) break;
                    await paint();
                    exports.Probe.AssetChunk(chunk);
                    bytes += chunk.length;
                }
                // Proxies may compress the response; fetch yields decoded bytes.
                const expected = Number(response.headers.get('x-slop-decoded-length'));
                if (!expected || bytes !== expected) throw new Error(`Incomplete asset transfer: ${bytes}/${expected}`);
                exports.Probe.EndAssetUpload(true);
                committed = true;
                await archive.stored;
                assetTransfer = { bytes, destination: '/Assets.zip', persistent: false };
            } finally {
                if (!committed) {
                    if (reader) void reader.cancel().catch(() => {});
                    exports.Probe.EndAssetUpload(false);
                }
                reader?.releaseLock();
            }
        }

        {
            // InstallPlatform resolves mod field types before Entry starts. The
            // interpreter can only bind dependencies already loaded from bytes.
            stage = 'Installing mod assemblies and platform';
            for (const name of ['0Harmony', 'Google.Protobuf', 'Markdig', 'AngleSharp', 'Tomlyn', 'SlopWorld']) {
                await loadFile(`./mod/${name}.dll`);
            }
            loaded.push(await loadFile('./Adapters.dll'));
            runtime.setModuleImports('jukebox-likes', jukeboxLikes(window));
            exports.Probe.InstallPlatform(new URL('.', window.location.href).href);
            stage = 'Installing assets/Core.zip and language';
            const response = await fetch('./assets/Core.zip');
            if (!response.ok) throw new Error(`Core.zip: HTTP ${response.status}`);
            const core = new Uint8Array(await response.arrayBuffer());
            await paint();
            exports.Probe.InstallCore(core);
            const enableLanguageMenu = await installCoreLanguage(exports.Probe, parameters, window, fetch);
            try {
                await paint();
                stage = 'Starting the game entry point';
                exports.Probe.StartEntry();
                let ready = false;
                for (let frame = 0; frame < 5; frame++) {
                    await paint(true);
                    ready = exports.Probe.EntryFrame(frame);
                }
                if (!ready) throw new Error('Entry did not finish loading during startup');
                enableLanguageMenu();
                const viewport = workspaceViewport(parameters);
                exports.Probe.ConfigureWorkspaceViewport(viewport.width, viewport.height, viewport.uiScale);
                document.documentElement.style.setProperty('--workspace-aspect', viewport.width / viewport.height);
                stage = 'Installing mod/Content.zip';
                const content = await fetch('./mod/Content.zip');
                if (!content.ok) throw new Error(`Content.zip: HTTP ${content.status}`);
                const modContent = new Uint8Array(await content.arrayBuffer());
                await paint();
                const textures = exports.Probe.InstallModContent(modContent);
                console.log('Browser: mounted ' + textures + ' mod textures');
                await paint();
                stage = 'Initializing the workspace';
                eco = exports.Probe.InitializeEco();
                let fontDefaults = null, fontChains = null;
                if (parameters.has('referenceAppearance')) {
                    const appearance = await fetch('./assets/reference-appearance.json');
                    if (!appearance.ok) throw new Error('Reference appearance is missing');
                    fontDefaults = await appearance.text();
                    exports.Probe.WorkspaceAppearance(fontDefaults);
                }
                if (parameters.get('localPreferences') !== 'off') {
                    (await loadModule('./workspace-preferences.js')).installWorkspacePreferences(exports.Probe);
                    (await loadModule('./workspace-keybindings.js')).installWorkspaceKeyBindings(exports.Probe);
                }
                if (parameters.has('nativeFonts')) {
                    await (await loadModule('./workspace-rasterizer.js')).installLocalRasterizer(exports.Probe);
                    const fonts = await loadModule('./workspace-fonts.js');
                    fontChains = await fonts.installStartupFonts(exports.Probe, fetch);
                }
                if (parameters.has('nativeFonts')) {
                    const fontLoader = await loadModule('./workspace-fonts.js');
                    const fonts = fontLoader.dynamicFonts(exports.Probe, fetch, undefined, fontDefaults ? {selection:fontDefaults, chains:fontChains} : null);
                    void fonts(exports.Probe.WorkspaceDynamicFonts(fonts));
                }
                await paint();
                stage = 'Rendering the first workspace frame';
                let commands = JSON.parse(await exports.Probe.WorkspaceFrame(new URL('.', window.location.href).href, parameters.get('session') ?? ''));
                const drawing = await loadModule('./gui.js');
                const canvas = document.createElement('canvas'); canvas.width = viewport.width; canvas.height = viewport.height;
                document.body.prepend(canvas);
                // Packets carry resource changes, not standalone snapshots. Apply
                // every packet before a font redraw can refer to its image uploads
                // or retained targets. The loading screen still covers this canvas.
                drawing.renderGui(canvas, commands);
                let frames = 1;
                if (parameters.has('nativeFonts') && await (await loadModule('./font-delivery.js')).warmWorkspaceFonts(exports.Probe)) {
                    await paint();
                    commands = JSON.parse(exports.Probe.WorkspaceUpdate('[]', 0)).commands;
                    drawing.renderGui(canvas, commands);
                    frames++;
                }
                workspace = {frames, commands: commands.length, textures};
                if (parameters.has('profile')) {
                    entry = entryReport(exports.Probe);
                    Object.assign(workspace, (await loadModule('./workspace-diagnostics.js')).inspectWorkspaceCanvas(canvas));
                    workspace.fonts = JSON.parse(exports.Probe.WorkspaceFontSnapshot());
                    workspace.game = JSON.parse(exports.Probe.GameSnapshot());
                }
                workspace.live = (await loadModule('./workspace.js')).startWorkspace(exports.Probe, canvas, 0);
                document.body.classList.add('ready');
                window.__loading?.finish();

            } catch (error) {
                entry ??= entryReport(exports.Probe);
                exports.Probe.StopEntry();
                throw error;
            }
        }
        window.__probeResult = { loaded, loadErrors, manifest, missingDependencies,
            workspace, entry, eco, assetTransfer, adaptations };
    } catch (error) {
        window.__probeResult = { error: String(error), stage, stack: error?.stack, entry };
    }
    document.getElementById('result').textContent = JSON.stringify(window.__probeResult, null, 2);
    if (window.__probeResult.error) {
        window.__loading?.finish();
        document.body.classList.remove('ready');
        document.getElementById('result').textContent = `Workspace could not start.\n${stage}\n${window.__probeResult.error}`;
    }
}
