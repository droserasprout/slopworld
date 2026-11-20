import { dotnet } from './_framework/dotnet.js';
import { workspaceViewport, workspaceParameters } from './workspace-viewport.js';
import { loadStartupAssemblies } from './startup-assemblies.js';
import { yieldStartupPaint } from './startup-yield.js';
import { installCoreLanguage } from './core-language.js';
let workspace = null;
let entry = null;
const parameters = workspaceParameters();
function requireChecks(report, stage) {
    const failed = report.checks.filter(check => !check.ok);
    if (failed.length)
        throw new Error(`${stage} failed: ` + failed.map(check =>
            `${check.name}: ${(check.errors ?? []).join('; ')}`).join('\n'));
}
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
    // AOT preparation replaces this boundary with published-identity loading.
    const assemblyRequest = fetch;
    const loadAssembly = (bytes, file) => exports.Probe.LoadAssembly(bytes);
    window.__loading?.status('Loading game assemblies...');
    const manifest = await fetch('./game/manifest.json').then(r => r.json());
    const {loaded, loadErrors} = await loadStartupAssemblies(manifest, loadAssembly, assemblyRequest);
    if (loadErrors.length) {
        const details = loadErrors.map(({file, error}) => `${file}: ${error}`).join('\n');
        throw new Error('Failed to load game assemblies:\n' + details);
    }
    const [missingDependencies, adaptations] = await Promise.all([
        fetch('./game/missing.json').then(r => r.json()),
        fetch('./game/adaptations.json').then(r => r.json())
    ]);
    window.__managedProbe = exports.Probe;
    async function loadFile(path) {
        const file = path.split('/').pop();
        let bytes;
        if (assemblyRequest) {
            const response = await assemblyRequest(path);
            if (!response.ok) throw new Error(file + ': HTTP ' + response.status);
            bytes = new Uint8Array(await response.arrayBuffer());
        }
        await yieldStartupPaint();
        return loadAssembly(bytes, file);
    }
    let eco = null;
    async function initializeEco() {
        await loadFile('./mod/SlopWorld.dll');
        await yieldStartupPaint();
        eco = JSON.parse(exports.Probe.InitializeEco());
        requireChecks(eco, 'Workspace initialization');
    }
    let assetTransfer = null;
    {
        const archive = await (await import('./asset-archive.js')).openAssetArchive('./assets/Eco.zip');
        const response = archive.response;
        if (!response.ok) throw new Error(`Eco.zip: HTTP ${response.status}`);
        exports.Probe.BeginAssetUpload();
        let bytes = 0, committed = false;
        try {
            for await (const chunk of response.body) {
                await yieldStartupPaint();
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
        const response = await fetch('./assets/Core.zip');
        if (!response.ok) throw new Error(`Core.zip: HTTP ${response.status}`);
        const core = new Uint8Array(await response.arrayBuffer());
        await yieldStartupPaint();
        const files = exports.Probe.InstallCore(core);
        const enableLanguageMenu = await installCoreLanguage(exports.Probe, parameters);
        try {
            await yieldStartupPaint();
            entry = JSON.parse(exports.Probe.StartEntry());
            requireChecks(entry, 'Entry initialization');
            for (let frame = 0; frame < 5; frame++) {
                await yieldStartupPaint(true);
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
                await yieldStartupPaint();
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
                        await (await import('./workspace-rasterizer.js')).installWorkspaceFont(exports.Probe,font,descriptor,face.index??-1);
                        const family=JSON.parse(descriptor).family;
                        if (family) families.add(family);
                    }
                    await (await import('./font-delivery.js')).warmWorkspaceFonts(exports.Probe,[...families]);
                }
                for (const name of ['0Harmony', 'Google.Protobuf', 'Markdig', 'AngleSharp', 'Tomlyn']) {
                    await loadFile(`./mod/${name}.dll`);
                }
                if (parameters.has('nativeFonts')) {
                    const fontLoader = await import('./workspace-fonts.js');
                    const fonts = fontLoader.dynamicFonts(exports.Probe, globalThis.fetch, () => workspaceViewport(parameters).uiScale, undefined, fontDefaults ? {selection:fontDefaults, chains:fontChains} : null);
                    void fonts(exports.Probe.WorkspaceDynamicFonts(fonts));
                }
                await yieldStartupPaint();
                let commands = JSON.parse(await exports.Probe.WorkspaceFrame(location.origin, parameters.get('session') ?? ''));
                const drawing = await import('./gui.js');
                const canvas = document.createElement('canvas'); canvas.width = viewport.width; canvas.height = viewport.height;
                document.body.prepend(canvas);
                // Packets carry resource changes, not standalone snapshots. Apply
                // every packet before a font redraw can refer to its image uploads
                // or retained targets. The loading screen still covers this canvas.
                drawing.renderGui(canvas, commands);
                let frames = 1;
                if (parameters.has('nativeFonts') && await (await import('./font-delivery.js')).warmWorkspaceFonts(exports.Probe)) {
                    await yieldStartupPaint();
                    commands = JSON.parse(exports.Probe.WorkspaceUpdate('[]', 0)).commands;
                    drawing.renderGui(canvas, commands);
                    frames++;
                }
                workspace = {frames, commands: commands.length, textures};
                if (parameters.has('profile')) {
                    Object.assign(workspace, drawing.renderGuiProbe(commands, false, canvas));
                    workspace.fonts = JSON.parse(exports.Probe.WorkspaceFontSnapshot());
                    workspace.game = JSON.parse(exports.Probe.GameSnapshot());
                }
                workspace.live = (await import('./workspace.js')).startWorkspace(exports.Probe, canvas, 0);
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
