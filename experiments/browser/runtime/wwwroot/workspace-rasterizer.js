// Synchronous WASM calls after asynchronous module/font-file loading. Font bytes
// stay alive until their FT_Face is closed; every returned bitmap is an owned copy.
export function localRasterizer(wasm) {
  const faces = new Map();
  let bytes = 0;
  function close() {
    for (const face of faces.values()) wasm._font_close(face.handle);
    faces.clear(); bytes = 0; wasm._font_shutdown();
  }
  function call(json) {
    const request = JSON.parse(json);
    if (request.close) { close(); return '{}'; }
    let face = faces.get(request.hash);
    if (request.font && !face) {
      const data = Uint8Array.from(atob(request.font), c => c.charCodeAt(0));
      if (faces.size >= 128 || bytes + data.length > 64 * 1024 * 1024) throw Error('Font source limit reached');
      const pointer = wasm._malloc(data.length);
      if (!pointer) throw Error('Font allocation failed');
      wasm.HEAPU8.set(data, pointer);
      const handle = wasm._font_open(pointer, data.length);
      if (!handle) { wasm._free(pointer); throw Error('Invalid outline font'); }
      face = {handle}; faces.set(request.hash, face); bytes += data.length;
    }
    if (!face) throw Error('Font is not loaded');
    if (request.scalar === undefined) {
      if (request.size === undefined) return '{}';
      if (wasm._font_size(face.handle, request.size, request.density, request.flags) < 0) throw Error('Invalid font size');
      const p = wasm._font_result() / 4;
      return JSON.stringify({ascent:wasm.HEAPF32[p+3],descent:wasm.HEAPF32[p+4]});
    }
    const status = wasm._font_glyph(face.handle, request.scalar, request.size, request.density, request.flags);
    if (status < 0) throw Error('Local font rasterization failed');
    if (!status) return '{"missing":true}';
    const p = wasm._font_result(), f = p / 4, i = wasm.HEAP32, v = wasm.HEAPF32;
    const width = i[f + 5], height = i[f + 6], pitch = i[f + 7], pixels = i[f + 8];
    let alpha = '';
    for (let y = 0; y < height; y++)
      for (let x = 0; x < width; x++) alpha += String.fromCharCode(wasm.HEAPU8[pixels + y * pitch + x]);
    return JSON.stringify({advance:v[f], x:v[f+1], y:v[f+2], ascent:v[f+3], descent:v[f+4],
      width, height, pixels:btoa(alpha)});
  }
  return {call, close};
}

export async function installLocalRasterizer(probe) {
  const {default:create} = await import('./local-fonts.mjs');
  const rasterizer = localRasterizer(await create());
  probe.WorkspaceRasterizer(rasterizer.call);
  globalThis.addEventListener?.('pagehide', event => { if (!event.persisted) probe.WorkspaceFontsDispose(); });
  return rasterizer;
}
