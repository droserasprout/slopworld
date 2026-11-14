// Overlap network/body reads, but keep CLR loading in manifest order. A rolling
// window bounds both in-flight downloads and completed buffers behind a slow file.
import {yieldStartupPaint} from './startup-yield.js';
export async function loadStartupAssemblies(manifest, load, request=globalThis.fetch, yieldPaint=yieldStartupPaint) {
  const download=async item=>{
    try {
      const response=await request(`./game/${item.file}`);
      if(!response.ok)throw Error(`${item.file}: HTTP ${response.status}`);
      return {bytes:new Uint8Array(await response.arrayBuffer())};
    } catch(error) {
      // A later request can fail before its turn. Settle it immediately so there
      // is no unhandled rejection while an earlier assembly is still downloading.
      return {error};
    }
  };
  const pending=manifest.slice(0,4).map(download),loaded=[],loadErrors=[];
  for(let index=0;index<manifest.length;index++) {
    const slot=index%4;
    const result=await pending[slot];
    pending[slot]=null;
    await yieldPaint();
    try {
      if('error' in result)throw result.error;
      loaded.push(load(result.bytes,manifest[index].file));
    } catch(error) {loadErrors.push({file:manifest[index].file,error:String(error)});}
    if(index+4<manifest.length)pending[slot]=download(manifest[index+4]);
  }
  return {loaded,loadErrors};
}
