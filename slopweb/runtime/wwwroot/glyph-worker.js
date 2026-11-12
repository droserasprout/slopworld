// FreeType misses and persistent cache I/O stay outside the UI event loop.
import create from './local-fonts.mjs';
import {localRasterizer} from './workspace-rasterizer.js';
import {GlyphStorage} from './glyph-storage.js';
const ready=create().then(localRasterizer), storage=new GlyphStorage();
let queue=Promise.resolve();
self.onmessage=({data})=>{
  queue=queue.then(async()=>{
    try {
      const raster=await ready;
      if (data.font) raster.install(data.hash,data.font);
      const value=data.request ? await storage.render(data.request,raster.call) : '{}';
      self.postMessage({id:data.id,value});
    } catch(error) { self.postMessage({id:data.id,error:String(error)}); }
  });
};
