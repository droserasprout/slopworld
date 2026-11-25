// Worker-side canvas and bitmap stand-ins. Exercise the real renderer and handoff
// without starting a browser. ArrayBuffers stand in for transferable ImageBitmaps.
import {parentPort,workerData} from 'node:worker_threads';
const paints=new Int32Array(workerData);
globalThis.location=new URL('https://example.test/slopweb-dev/loading-worker.js');
globalThis.self={
  postMessage:(message,transfer)=>parentPort.postMessage(message,transfer),
  close:()=>parentPort.close(),
};
globalThis.requestAnimationFrame=callback=>setTimeout(callback,8);
globalThis.cancelAnimationFrame=clearTimeout;
globalThis.ImageData=class {};
globalThis.OffscreenCanvas=class {
  getContext() {
    return new Proxy({}, {get:(_,key)=>key==='getImageData'
      ? ()=>({data:new Uint8ClampedArray(4)})
      : ()=>{}});
  }
};
globalThis.createImageBitmap=async ()=>{
  const bitmap=new ArrayBuffer(4);
  Object.assign(bitmap,{width:1,height:1,close(){}});
  return bitmap;
};
globalThis.fetch=async url=>{
  if(String(url).endsWith('manifest.json'))return Response.json({
    width:1,height:1,original:'frame.png',cached:true,frames:['frame.png'],
    animation:{onsetSeconds:1,onset:1,depths:1,phases:1,offsetX:0,offsetY:0}
  });
  return new Response('sample quote');
};
await import('../../runtime/wwwroot/loading-worker.js');
parentPort.on('message',data=>{
  if(data.type==='start'){
    const canvas=new OffscreenCanvas();
    const context=canvas.getContext('2d');
    canvas.getContext=()=>new Proxy(context,{get:(target,key)=>{
      if(key==='fillRect')return ()=>{
        Atomics.add(paints,0,1);
        Atomics.notify(paints,0);
      };
      return target[key];
    }});
    data.canvas=canvas;
  }
  self.onmessage({data});
});
