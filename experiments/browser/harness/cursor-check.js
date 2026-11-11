import {cursorImage} from './cursor-image.js';
import {attachWorkspaceCursor} from './workspace-cursor.js';
try {
  const source=document.createElement('canvas');source.width=source.height=48;
  const context=source.getContext('2d');context.fillStyle='#ac3917';context.fillRect(4,6,20,30);
  const expected=context.getImageData(0,0,48,48).data;
  const url=cursorImage(source.toDataURL(),48,48,4,6);
  const bitmap=new Image();bitmap.src=url;await bitmap.decode();
  if(bitmap.naturalWidth!==48||bitmap.naturalHeight!==48)throw Error('CUR dimensions changed');
  const decoded=document.createElement('canvas');decoded.width=decoded.height=48;
  decoded.getContext('2d').drawImage(bitmap,0,0);
  const pixels=decoded.getContext('2d').getImageData(0,0,48,48).data;
  if(pixels.some((value,i)=>value!==expected[i]))throw Error('CUR changed native pixels');
  const canvas=document.createElement('canvas');document.body.append(canvas);
  const original=Object.getOwnPropertyDescriptor(window,'devicePixelRatio');
  let update=JSON.stringify({id:1,width:48,height:48,x:4,y:6,
    pixels:btoa(Array.from(expected,v=>String.fromCharCode(v)).join(''))});
  const cursor=attachWorkspaceCursor(canvas,{WorkspaceCursorTake:()=>{const result=update;update='';return result;}});
  const overlay=document.querySelector('canvas[aria-hidden="true"]');
  try {
    for(const density of [1,5/3,2]) {
      Object.defineProperty(window,'devicePixelRatio',{value:density,configurable:true});
      cursor.frame();
      canvas.dispatchEvent(new PointerEvent('pointermove',{clientX:100,clientY:100}));
      if(!getComputedStyle(canvas).cursor.startsWith('image-set(')||overlay.style.display!=='none')
        throw Error('Native cursor unavailable at density '+density);
      canvas.dispatchEvent(new PointerEvent('pointermove',{clientX:innerWidth-1,clientY:100}));
      if(canvas.style.cursor!=='none'||overlay.style.display!=='block')throw Error('Edge fallback missing');
      if(Math.abs(parseFloat(overlay.style.width)-48/density)>1e-6)throw Error('Physical cursor size changed');
      canvas.dispatchEvent(new PointerEvent('pointerleave'));
      if(overlay.style.display!=='none')throw Error('Cursor remained outside canvas');
    }
  } finally {
    cursor.dispose();
    if(original)Object.defineProperty(window,'devicePixelRatio',original);else delete window.devicePixelRatio;
    canvas.remove();
  }
  if(document.querySelector('canvas[aria-hidden="true"]'))throw Error('Cursor overlay leaked');
  window.__probeResult={status:'ok',pixelExact:true,densities:[1,5/3,2],nativeCursor:true,edgeFallback:true};
} catch(error) {window.__probeResult={status:'fail',error:String(error),stack:error.stack};}
