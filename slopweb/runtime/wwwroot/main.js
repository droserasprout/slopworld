import { publishedAssemblies } from './assembly-mode.js';
import { nativeWasmResource } from './native-wasm.js';
import { workspaceViewport, workspaceParameters } from './workspace-viewport.js';
import { loadStartupAssembly, loadStartupAssemblies } from './startup-assemblies.js';
import { yieldStartupPaint } from './startup-yield.js';
import { installCoreLanguage } from './core-language.js';
function requireChecks(report, stage) {
    const failed = report.checks.filter(check => !check.ok);
    if (failed.length)
        throw new Error(`${stage} failed: ` + failed.map(check =>
            `${check.name}: ${(check.errors ?? []).join('; ')}`).join('\n'));
}

export async function startRuntime(dotnet, parameters = workspaceParameters(), {
    window = globalThis.window, document = window.document, fetch = globalThis.fetch,
    published = publishedAssemblies, paint = yieldStartupPaint, loadModule = path => import(path),
} = {}) {
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
        async function initializeEco() {
            await paint();
            eco = JSON.parse(exports.Probe.InitializeEco());
            requireChecks(eco, 'Workspace initialization');
        }
        let assetTransfer = null;
        {
            const archive = await (await loadModule('./asset-archive.js')).openAssetArchive('./assets/Eco.zip');
            const response = archive.response;
            if (!response.ok) throw new Error(`Eco.zip: HTTP ${response.status}`);
            exports.Probe.BeginAssetUpload();
            let bytes = 0, committed = false;
            try {
                for await (const chunk of response.body) {
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
                if (!committed) exports.Probe.EndAssetUpload(false);
            }
        }

        {
            loaded.push(await loadFile('./Adapters.dll'));
            // Woven game methods can resolve mod types during Entry startup.
            await loadFile('./mod/SlopWorld.dll');
            exports.Probe.InstallPlatform(new URL('.', window.location.href).href);
            const response = await fetch('./assets/Core.zip');
            if (!response.ok) throw new Error(`Core.zip: HTTP ${response.status}`);
            const core = new Uint8Array(await response.arrayBuffer());
            await paint();
            const files = exports.Probe.InstallCore(core);
            const enableLanguageMenu = await installCoreLanguage(exports.Probe, parameters, window, fetch);
            try {
                await paint();
                entry = JSON.parse(exports.Probe.StartEntry());
                requireChecks(entry, 'Entry initialization');
                for (let frame = 0; frame < 5; frame++) {
                    await paint(true);
                    entry = JSON.parse(exports.Probe.EntryFrame(frame));
                    requireChecks(entry, 'Entry initialization');
                }
                entry.files = files;
                if (entry.loaded) {
                    enableLanguageMenu();
                    const viewport = workspaceViewport(parameters);
                    exports.Probe.ConfigureWorkspaceViewport(viewport.width, viewport.height, viewport.uiScale);
                    document.documentElement.style.setProperty('--workspace-aspect', viewport.width / viewport.height);
                    const content = await fetch('./mod/Content.zip');
                    if (!content.ok) throw new Error('Workspace mod content is missing');
                    const modContent = new Uint8Array(await content.arrayBuffer());
                    await paint();
                    const textures = exports.Probe.InstallModContent(modContent);
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
                        (await loadModule('./workspace-preferences.js')).installWorkspacePreferences(exports.Probe);
                        (await loadModule('./workspace-keybindings.js')).installWorkspaceKeyBindings(exports.Probe);
                    }
                    if (parameters.has('nativeFonts')) {
                        await (await loadModule('./workspace-rasterizer.js')).installLocalRasterizer(exports.Probe);
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
                            const base = new URL('./assets/fonts/', window.location.href);
                            const fontUrl = new URL(face.font, base), bakeUrl = new URL(face.bake, base);
                            if (!fontUrl.href.startsWith(base.href) || !bakeUrl.href.startsWith(base.href))
                                throw new Error('Font manifest path escapes local font directory');
                            if (!fonts.has(fontUrl.href)) fonts.set(fontUrl.href, face.hash
                                ? (await loadModule('./host-fonts.js')).hostFonts(exports.Probe).file(face.hash)
                                : loadFont(fontUrl));
                            const [font, bake] = await Promise.all([fonts.get(fontUrl.href), fetch(bakeUrl)]);
                            if (!bake.ok) throw new Error('Host font descriptor is unavailable');
                            const descriptor=await bake.text();
                            await (await loadModule('./workspace-rasterizer.js')).installWorkspaceFont(exports.Probe,font,descriptor,face.index??-1);
                            const family=JSON.parse(descriptor).family;
                            if (family) families.add(family);
                        }
                        await (await loadModule('./font-delivery.js')).warmWorkspaceFonts(exports.Probe,[...families]);
                    }
                    for (const name of ['0Harmony', 'Google.Protobuf', 'Markdig', 'AngleSharp', 'Tomlyn']) {
                        await loadFile(`./mod/${name}.dll`);
                    }
                    if (parameters.has('nativeFonts')) {
                        const fontLoader = await loadModule('./workspace-fonts.js');
                        const fonts = fontLoader.dynamicFonts(exports.Probe, fetch, undefined, fontDefaults ? {selection:fontDefaults, chains:fontChains} : null);
                        void fonts(exports.Probe.WorkspaceDynamicFonts(fonts));
                    }
                    await paint();
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
                        Object.assign(workspace, (await loadModule('./workspace-diagnostics.js')).inspectWorkspaceCanvas(canvas));
                        workspace.fonts = JSON.parse(exports.Probe.WorkspaceFontSnapshot());
                        workspace.game = JSON.parse(exports.Probe.GameSnapshot());
                    }
                    workspace.live = (await loadModule('./workspace.js')).startWorkspace(exports.Probe, canvas, 0);
                    document.body.classList.add('ready');
                    window.__loading?.finish();
                } else throw new Error('Entry did not finish loading during startup');

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
}
