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
    return new Proxy({}, {get:(state,key)=>key in state ? state[key] : key==='getImageData'
      ? ()=>({data:new Uint8ClampedArray(4)})
      : ()=>{}});
  }
};
globalThis.createImageBitmap=async blob=>{
  const bitmap=new ArrayBuffer(4);
  Object.assign(bitmap,{name:await blob.text(),width:1,height:1,close(){}});
  return bitmap;
};
let finishDownload;
const finalFrame=new Promise(resolve=>{finishDownload=resolve;});
globalThis.fetch=async url=>{
  if(String(url).endsWith('manifest.json'))return Response.json({
    width:1,height:1,original:'one',cached:true,frames:['one','two','three','four','five','six'],
    framePacks:[{file:'starter.bin',lengths:[3,3,5,4]},{file:'rest.bin',lengths:[4,3]}],
    animation:{onsetSeconds:.04,onset:2,depths:2,phases:2,step:1,pull:0,jitter:0,gapMax:8,phaseSeconds:.02,offsetX:0,offsetY:0}
  });
  if(String(url).endsWith('/starter.bin'))return new Response('onetwothreefour');
  if(String(url).endsWith('/rest.bin')){await finalFrame;return new Response('fivesix');}
  return new Response(String(url));
};
await import('../../runtime/wwwroot/loading-worker.js');
parentPort.on('message',data=>{
  if(data.type==='start'){
    const canvas=new OffscreenCanvas();
    const context=canvas.getContext('2d');
    let lastLoopFrame;
    canvas.getContext=()=>new Proxy(context,{get:(target,key)=>{
      if(key==='drawImage')return (image,x,y,w,h)=>{
        if(target.globalAlpha!==.5 || !['three','four'].includes(image?.name))return;
        // Only count changes to the current loop layer drawn on the visible canvas.
        if(x>=800 || y>=600 || x+w<=0 || y+h<=0)throw Error('Background outside viewport');
        if(image.name!==lastLoopFrame){lastLoopFrame=image.name;Atomics.add(paints,1,1);}
      };
      if(key==='fillRect')return ()=>{
        Atomics.add(paints,0,1);
        Atomics.notify(paints,0);
      };
      return target[key];
    }});
    data.canvas=canvas;
  }
  self.onmessage({data});
  if(data.type==='take')finishDownload();
});
