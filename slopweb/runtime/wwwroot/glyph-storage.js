// Version this namespace when FreeType or its rasterization contract changes.
// Each entry is bounded; 512 entries retain at most 32 MiB of glyph JSON.
import {browserCaches} from './browser-storage.js';
export class GlyphStorage {
  constructor(storage=browserCaches()) {
    this.storage=storage;this.pending=new Map();this.writing=null;
  }
  key(request) {
    return '/__slopweb_glyphs/v1/'+encodeURIComponent(JSON.stringify([
      request.hash,request.scalar,request.size,request.density,request.flags]));
  }
  async render(request, rasterize) {
    const key=this.key(request);
    if(this.pending.has(key))return this.pending.get(key).value;
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
    // Persistence is optional and must not hold the worker's next glyph or font
    // installation behind disk writes. Bound queued JSON to 512 * 64 Ki characters;
    // a slow store loses reuse, never rendering or unbounded worker memory.
    if (cache && validGlyph(value) && this.pending.size<512) {
      this.pending.set(key,{cache,value});
      this.startWrite();
    }
    return value;
  }
  startWrite() {
    this.writing??=this.persist().finally(()=>{
      this.writing=null;
      if(this.pending.size)this.startWrite();
    });
  }
  async persist() {
    while(this.pending.size) {
      const cache=this.pending.values().next().value.cache;
      // Prune once per drained batch, rather than enumerating the entire disk
      // cache after every glyph while startup is still adding writes.
      let written=0;
      while(this.pending.size && written++<512) {
        const [key,{cache,value}]=this.pending.entries().next().value;
        try {
          await cache.put(key,new Response(value));
        } catch { /* Quota errors only lose reuse on reload. */ }
        finally {this.pending.delete(key);}
      }
      try {
        const keys=await cache.keys();
        for(const old of keys.slice(0,Math.max(0,keys.length-512)))await cache.delete(old);
      } catch { /* Storage may become unavailable during cleanup. */ }
    }
  }
  async flush() {while(this.writing)await this.writing;}
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
