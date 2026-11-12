// Version this namespace when FreeType or its rasterization contract changes.
// Each entry is bounded; 512 entries retain at most 32 MiB of glyph JSON.
export class GlyphStorage {
  constructor(storage=globalThis.caches) { this.storage=storage; }
  key(request) {
    return '/__slopweb_glyphs/v1/'+encodeURIComponent(JSON.stringify([
      request.hash,request.scalar,request.size,request.density,request.flags]));
  }
  async render(request, rasterize) {
    const key=this.key(request);
    let cache;
    try {
      cache=await this.storage?.open('slopweb.glyphs.v1');
      const response=await cache?.match(key);
      if (response) {
        const value=await response.text();
        if (validGlyph(value)) return value;
        await cache.delete(key);
      }
    } catch { /* Denied storage must not prevent local rendering. */ }
    const value=rasterize(JSON.stringify(request));
    if (validGlyph(value)) {
      try {
        await cache?.put(key,new Response(value));
        const keys=await cache?.keys()??[];
        for(const old of keys.slice(0,Math.max(0,keys.length-512))) await cache.delete(old);
      } catch { /* Quota errors only lose reuse on reload. */ }
    }
    return value;
  }
}

export function validGlyph(value) {
  if (typeof value!=='string' || value.length>65536) return false;
  try {
    const glyph=JSON.parse(value);
    if (glyph.missing===true) return true;
    if (!['advance','x','y','ascent','descent'].every(key=>Number.isFinite(glyph[key]))) return false;
    if (![glyph.width,glyph.height].every(n=>Number.isInteger(n)&&n>=0&&n<=4096)) return false;
    return typeof glyph.pixels==='string' && atob(glyph.pixels).length===glyph.width*glyph.height;
  } catch { return false; }
}
