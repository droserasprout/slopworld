// The transferred canvas presents directly from this worker while Mono occupies
// the window thread. No frame depends on a window callback or message round trip.
import {renderLoading} from './loading-renderer.js';

let renderer;
self.onmessage=({data})=>{
  switch(data.type) {
    case 'start':
      renderer=renderLoading(data.canvas,data.viewport,data.referenceAppearance,
        (width,height)=>new OffscreenCanvas(width,height),
        async signal=>{
          const response=await fetch('./mod/LoadingFont.png',{signal});
          if(!response.ok)throw Error('Loading atlas unavailable');
          return createImageBitmap(await response.blob());
        },(key,value)=>self.postMessage({type:'report',key,value}));
      break;
    case 'resize': renderer.resize(data.viewport); break;
    case 'status': renderer.status(data.text); break;
    case 'take':
      // Stop drawing immediately, but keep pending frame downloads alive.
      const retained=renderer.takeBackground();
      renderer.finish();
      retained.ready.then(value=>{
        const frames=value?.animation.frames;
        self.postMessage({type:'background',frames,
          started:value ? performance.timeOrigin+value.started*1000 : 0},frames?.images ?? []);
      }).catch(()=>self.postMessage({type:'background'})).finally(()=>self.close());
      break;
  }
};
