// Pre-WASM playback of locally prepared native frames. The manifest obtains its
// source key, preset and tuning from installed code, never from a guessed cache.
export function backgroundAnimation(tuning, random=Math.random) {
  let depth=.5, phase=0, walkAt=0, phaseAt=0, current=null, previous=null;
  return seconds=>{
    let index;
    if(seconds<tuning.onsetSeconds) {
      const t=Math.max(0,seconds/tuning.onsetSeconds), u=t*t*(3-2*t);
      index=Math.min(tuning.onset-1,Math.floor(u*tuning.onset));
    } else {
      const t=seconds-tuning.onsetSeconds;
      if(tuning.depths>1) {
        if(t-walkAt>tuning.gapMax) walkAt=t-tuning.step;
        while(t-walkAt>=tuning.step) {
          walkAt+=tuning.step;
          depth=Math.max(0,Math.min(1,depth+(.5-depth)*tuning.pull+
            tuning.jitter*(random()+random()+random()-1.5)));
        }
      }
      if(tuning.phases>1 && t-phaseAt>=tuning.phaseSeconds) {
        phaseAt=t;
        phase=(phase+1+Math.floor(random()*(tuning.phases-1)))%tuning.phases;
      }
      index=tuning.onset+Math.round(depth*(tuning.depths-1))*tuning.phases+phase;
    }
    if(index!==current) {previous=current??index;current=index;}
    return {current,previous};
  };
}

export async function loadBackground(signal) {
  const base=new URL('./assets/background/',location.href);
  const response=await fetch(new URL('manifest.json',base),{signal});
  if(!response.ok) return null;
  const manifest=await response.json();
  if(!manifest.cached || !manifest.animation || !manifest.frames.length) return null;
  const images=[];
  try {
    // Decode one frame at a time; do not publish a partial frame set.
    for(const name of manifest.frames) {
      const frame=await fetch(new URL(name,base),{signal});
      if(!frame.ok) throw new Error('Background frame unavailable');
      images.push(await createImageBitmap(await frame.blob()));
    }
    if(signal.aborted) throw new DOMException('Aborted','AbortError');
  } catch(error) {for(const image of images) image.close();throw error;}
  const select=backgroundAnimation(manifest.animation);
  let frame=select(0);
  return {
    advance(seconds) {
      const next=select(seconds), changed=next.current!==frame.current || next.previous!==frame.previous;
      frame=next;return changed;
    },
    draw(context,width,height) {
      const aspect=manifest.width/manifest.height;
      const w=Math.max(width,height*aspect), h=w/aspect;
      const x=Math.floor(width/2)-w/2, y=Math.floor(height/2)-h/2;
      const {offsetX,offsetY}=manifest.animation;
      context.save();
      context.globalAlpha=1;
      context.drawImage(images[frame.previous],x-offsetX,y-offsetY,w,h);
      context.globalAlpha=.5;
      context.drawImage(images[frame.current],x+offsetX,y+offsetY,w,h);
      context.restore();
    },
    dispose() {for(const image of images) image.close();},
  };
}
