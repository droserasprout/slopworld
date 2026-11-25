// The page owns the loading canvas element; its renderer owns the pixels.
import {workspaceViewport} from './workspace-viewport.js';
import {renderLoading} from './loading-renderer.js';
import {backgroundPlayer} from './loading-background.js';

function pageRenderer(canvas,viewport,referenceAppearance) {
  return renderLoading(canvas,viewport,referenceAppearance,(w,h)=>{
    const surface=document.createElement('canvas');
    surface.width=w;surface.height=h;return surface;
  },async ()=>{
    const atlas=new Image();
    atlas.src='./mod/LoadingFont.png';
    await atlas.decode();
    return atlas;
  },(key,value)=>{canvas.dataset[key]=value;});
}

function workerRenderer(canvas,viewport,referenceAppearance,onFailure) {
  const worker=new Worker(new URL('./loading-worker.js',import.meta.url),{type:'module'});
  let settle,transferred=false;
  const ready=new Promise(resolve=>{settle=resolve;});
  const cancel=()=>{worker.terminate();settle(null);};
  worker.onmessage=({data})=>{
    if(data.type==='report')canvas.dataset[data.key]=data.value;
    if(data.type==='background') {
      const {frames,started}=data;
      settle(frames ? {animation:backgroundPlayer(frames.manifest,frames.images,frames.state),
        started:(started-performance.timeOrigin)/1000} : null);
      worker.terminate();
    }
  };
  worker.onerror=()=>{cancel();if(!transferred)onFailure();};
  try {
    const surface=canvas.transferControlToOffscreen();
    worker.postMessage({type:'start',canvas:surface,viewport,referenceAppearance},[surface]);
  } catch(error) {
    cancel();throw error;
  }
  return {
    resize:viewport=>worker.postMessage({type:'resize',viewport}),
    status:text=>worker.postMessage({type:'status',text}),
    takeBackground() {transferred=true;worker.postMessage({type:'take'});return {ready,cancel};},
    finish() {if(!transferred)cancel();},
  };
}

export async function startLoading(parameters) {
  let viewport=workspaceViewport(parameters);
  const referenceAppearance=parameters.has('referenceAppearance');
  document.title='SlopWorld';
  document.body.classList.add('workspace-app','loading');
  const setAspect=()=>document.body.style.setProperty('--workspace-aspect',viewport.width/viewport.height);
  setAspect();
  let message='Loading SlopWorld...',finished=false,renderer;
  function makeCanvas() {
    const canvas=document.createElement('canvas');
    canvas.id='loading-screen';
    canvas.width=viewport.width;canvas.height=viewport.height;
    canvas.setAttribute('aria-label',message);
    canvas.setAttribute('role','img');
    document.body.prepend(canvas);
    return canvas;
  }
  let canvas=makeCanvas();
  const fallback=()=>{
    if(finished)return;
    // A transferred canvas cannot acquire a context on the window thread.
    canvas.remove();canvas=makeCanvas();
    renderer=pageRenderer(canvas,viewport,referenceAppearance);
    renderer.status(message);
  };
  if(typeof Worker==='function' && canvas.transferControlToOffscreen) {
    try {renderer=workerRenderer(canvas,viewport,referenceAppearance,fallback);}
    catch {fallback();}
  } else renderer=pageRenderer(canvas,viewport,referenceAppearance);
  const resize=()=>{
    viewport=workspaceViewport(parameters);setAspect();renderer.resize(viewport);
  };
  if(parameters.has('responsive'))window.addEventListener('resize',resize);
  // Admit a paint before importing WASM; hidden tabs may suspend RAF.
  await new Promise(resolve=>{
    const timer=setTimeout(resolve,100);
    requestAnimationFrame(()=>requestAnimationFrame(()=>{clearTimeout(timer);resolve();}));
  });
  return {
    takeBackground:()=>renderer.takeBackground(),
    status(text) {message=text;canvas.setAttribute('aria-label',text);renderer.status(text);},
    finish() {
      finished=true;window.removeEventListener('resize',resize);
      renderer.finish();canvas.remove();document.body.classList.remove('loading');
    },
  };
}
