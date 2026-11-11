// Real Canvas upload timing and byte/pixel equivalence; no game or WASM.
import {decodeGuiPixels} from './gui-pixels.js';
import {renderGui,guiImageCount} from './gui.js';

const nativeDecoder = Uint8Array.fromBase64;
const reference = value => Uint8ClampedArray.from(atob(value), c => c.charCodeAt(0));
const make = side => {
  const canvas = document.createElement('canvas');
  canvas.width = side; canvas.height = side;
  return canvas;
};
const read = canvas => canvas.getContext('2d').getImageData(0,0,canvas.width,canvas.height).data;
const equal = (actual, expected, label) => {
  if(actual.length !== expected.length)throw new Error(`${label}: length differs`);
  for(let i=0;i<actual.length;i++)if(actual[i]!==expected[i])throw new Error(`${label}: byte ${i} differs`);
};
// Match production's fresh source canvas, decode, ImageData and putImageData.
// Readback is outside timed sections; timings do not measure GPU completion.
const upload = (encoded, side, decode) => {
  const canvas = make(side);
  canvas.getContext('2d').putImageData(new ImageData(decode(encoded),side,side),0,0);
  return canvas;
};
const encode = bytes => {
  let binary='';
  for(let i=0;i<bytes.length;i+=8192)binary+=String.fromCharCode(...bytes.subarray(i,i+8192));
  return btoa(binary);
};

try {
  const results=[], rounds=7, iterations=3;
  let scenarios=0, checksum=0;
  for(const side of [256,512,1024]) {
    // Include translucent and transparent texels to compare Canvas's actual
    // premultiplied-alpha round trip, rather than requiring original RGB back.
    const bytes=Uint8ClampedArray.from({length:side*side*4},(_,i)=>(i*73+(i>>8))%256);
    const encoded=encode(bytes), expected=read(upload(encoded,side,reference));
    const samples={reference:[],native:[],fallback:[]};
    for(const mode of ['native','fallback']) {
      if(mode==='native'&&typeof nativeDecoder!=='function')continue;
      Uint8Array.fromBase64=mode==='fallback'?undefined:nativeDecoder;
      equal(decodeGuiPixels(encoded),bytes,`${mode} decoded bytes`);
      equal(read(upload(encoded,side,decodeGuiPixels)),expected,`${mode} uploaded pixels`);
      // Exercise the real image command, draw and release path too. Compare
      // both through drawImage, since that can add another alpha conversion.
      const actual=make(side),referenceScreen=make(side);
      referenceScreen.getContext('2d').drawImage(upload(encoded,side,reference),0,0);
      const before=guiImageCount(),id=98761;
      try {
        renderGui(actual,[{kind:'image',value:{id,width:side,height:side,pixels:encoded}},
          {kind:'imageTexture',value:{id,color:[1,1,1,1],uv:[0,0,1,1],rect:[0,0,side,side]}}]);
        equal(read(actual),read(referenceScreen),`${mode} production draw`);
      } finally {renderGui(actual,[{kind:'releaseImage',value:id}]);}
      if(guiImageCount()!==before)throw new Error('Production check leaked an image');
      scenarios++;
    }
    for(let round=0;round<rounds;round++) {
      for(const mode of round%2?['fallback','native','reference']:['reference','native','fallback']) {
        if(mode==='native'&&typeof nativeDecoder!=='function')continue;
        Uint8Array.fromBase64=mode==='fallback'?undefined:nativeDecoder;
        const decode=mode==='reference'?reference:decodeGuiPixels;
        // Warm each path before measurement. Retain only the final canvas.
        upload(encoded,side,decode);
        let last;
        const start=performance.now();
        for(let i=0;i<iterations;i++)last=upload(encoded,side,decode);
        samples[mode].push((performance.now()-start)/iterations);
        checksum+=read(last)[127];
        // Let CDP read status between batches; the old decoder can block for
        // seconds on a large atlas under a CPU quota.
        await new Promise(resolve=>setTimeout(resolve,0));
      }
    }
    const medians=Object.fromEntries(Object.entries(samples).map(([mode,values])=>
      [mode,values.length?values.toSorted((a,b)=>a-b)[Math.floor(values.length/2)]:null]));
    results.push({side,bytes:bytes.length,medianMsPerUpload:medians,samples});
    // Yield between atlas sizes so the diagnostic remains responsive.
    await new Promise(resolve=>setTimeout(resolve,0));
  }
  window.__probeResult={status:'ok',byteExact:true,pixelExact:true,scenarios,rounds,iterations,
    measurement:'Fresh Canvas allocation + RGBA decode + ImageData + putImageData; excludes readback, GPU completion, WASM and workspace latency',
    userAgent:navigator.userAgent,contextAttributes:make(1).getContext('2d').getContextAttributes(),
    nativeBase64:typeof nativeDecoder==='function',results,checksum};
} catch(error) {window.__probeResult={status:'fail',error:String(error)};}
finally {Uint8Array.fromBase64=nativeDecoder;}
