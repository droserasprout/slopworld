// Overlap network/body reads, but keep CLR loading in manifest order. A rolling
// window bounds both in-flight downloads and completed buffers behind a slow file.
import {yieldStartupPaint} from './startup-yield.js';
async function downloadAssembly(path, request) {
  if (!request) return; // Published assemblies resolve by CLR identity.
  for (let attempt = 0; ; attempt++) {
    try {
      const response = await request(path, attempt ? {cache:'reload'} : undefined);
      if (!response.ok) throw Error(`HTTP ${response.status} for ${path}`);
      return new Uint8Array(await response.arrayBuffer());
    } catch (error) {
      // Fetch reports interrupted requests and truncated response bodies as TypeError.
      // Retry the download once, bypassing the HTTP cache, before any CLR install.
      if (attempt || !(error instanceof TypeError)) throw error;
    }
  }
}
async function installAssembly(bytes, file, load, yieldPaint) {
  await yieldPaint();
  return load(bytes, file);
}
export async function loadStartupAssembly(path, load, request=globalThis.fetch, yieldPaint=yieldStartupPaint) {
  return installAssembly(await downloadAssembly(path, request), path.split('/').pop(), load, yieldPaint);
}
export async function loadStartupAssemblies(manifest, load, request=globalThis.fetch, yieldPaint=yieldStartupPaint) {
  // Settle later failures immediately while earlier assemblies still download.
  const download = item => downloadAssembly(`./game/${item.file}`, request)
    .then(bytes => ({bytes}), error => ({error}));
  const pending=manifest.slice(0,4).map(download),loaded=[],loadErrors=[];
  for(let index=0;index<manifest.length;index++) {
    const slot=index%4;
    const result=await pending[slot];
    pending[slot]=null;
    try {
      if('error' in result)throw result.error;
      loaded.push(await installAssembly(result.bytes,manifest[index].file,load,yieldPaint));
    } catch(error) {loadErrors.push({file:manifest[index].file,error:String(error)});}
    if(index+4<manifest.length)pending[slot]=download(manifest[index+4]);
  }
  return {loaded,loadErrors};
}
