import { dotnet } from './_framework/dotnet.js';

try {
    const runtime = await dotnet.create();
    const exports = await runtime.getAssemblyExports('BrowserProbe.dll');
    const result = JSON.parse(await exports.BrowserProbe.RunAsync(new URL('index.html', location.href).href));
    try {
        const response = await fetch('./mod/SlopWorld.dll');
        if (!response.ok) throw new Error(`SlopWorld.dll: HTTP ${response.status}`);
        const bytes = new Uint8Array(await response.arrayBuffer());
        result.modAssemblyLoad = { ok: true, value: exports.BrowserProbe.LoadMod(bytes), bytes: bytes.length };
    } catch (error) {
        result.modAssemblyLoad = { ok: false, exception: String(error) };
    }
    globalThis.__probeResult = result;
} catch (error) {
    globalThis.__probeResult = { error: String(error), stack: error.stack };
}
document.getElementById('result').textContent = JSON.stringify(globalThis.__probeResult, null, 2);
