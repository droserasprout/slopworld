// Pre-WASM playback of locally prepared native frames. The manifest obtains its
// source key, preset and tuning from installed code, never from a guessed cache.
function initialPlayback() {
  return {depth:.5,phase:0,walkAt:0,phaseAt:0,current:null,previous:null};
}

export function backgroundAnimation(tuning, random=Math.random, state=initialPlayback()) {
  return (seconds,available=Infinity)=>{
    let index;
    if(seconds<tuning.onsetSeconds) {
      const t=Math.max(0,seconds/tuning.onsetSeconds), u=t*t*(3-2*t);
      index=Math.min(available-1,tuning.onset-1,Math.floor(u*tuning.onset));
    } else if(available<=tuning.onset) {
      // The intro plays once. Hold its latest decoded frame until a loop frame arrives.
      index=available-1;
    } else {
      const t=seconds-tuning.onsetSeconds;
      if(tuning.depths>1) {
        if(t-state.walkAt>tuning.gapMax) state.walkAt=t-tuning.step;
        while(t-state.walkAt>=tuning.step) {
          state.walkAt+=tuning.step;
          state.depth=Math.max(0,Math.min(1,state.depth+(.5-state.depth)*tuning.pull+
            tuning.jitter*(random()+random()+random()-1.5)));
        }
      }
      if(tuning.phases>1 && t-state.phaseAt>=tuning.phaseSeconds) {
        state.phaseAt=t;
        state.phase=(state.phase+1+Math.floor(random()*(tuning.phases-1)))%tuning.phases;
      }
      const decoded=available-tuning.onset;
      const depth=Math.min(Math.round(state.depth*(tuning.depths-1)),Math.max(0,Math.floor(decoded/tuning.phases)-1));
      const start=depth*tuning.phases;
      const phases=Math.min(tuning.phases,decoded-start);
      index=tuning.onset+start+state.phase%phases;
    }
    if(index!==state.current) {state.previous=state.current??index;state.current=index;}
    return {current:state.current,previous:state.previous};
  };
}

export async function loadBackground(signal, showOriginal=value=>value.dispose(), startPlayback=()=>{}) {
  const base=new URL('./assets/background/',location.href);
  const response=await fetch(new URL('manifest.json',base),{signal});
  if(!response.ok) return null;
  const manifest=await response.json();
  const fetchBlob=async name=>{
    const response=await fetch(new URL(name,base),{signal});
    if(!response.ok) throw new Error('Background image unavailable: '+name);
    return response.blob();
  };
  let previewBlob;
  if(manifest.original) {
    previewBlob=await fetchBlob(manifest.original);
    const image=await createImageBitmap(previewBlob);
    if(signal.aborted) {image.close();throw new DOMException('Aborted','AbortError');}
    showOriginal({
      draw(context,width,height) {
        const aspect=manifest.width/manifest.height;
        const w=Math.max(width,height*aspect), h=w/aspect;
        context.drawImage(image,(width-w)/2,(height-h)/2,w,h);
      },
      dispose() {image.close();},
    });
  }
  if(!manifest.cached || !manifest.animation || !manifest.frames.length) return null;
  const images=[];
  const player=backgroundPlayer(manifest,images);
  const playable=Math.min(manifest.frames.length,manifest.animation.onset+manifest.animation.phases);
  async function decode(blob) {
    images.push(await createImageBitmap(blob));
    signal.throwIfAborted();
    if(images.length===playable)startPlayback(player);
  }
  try {
    if(manifest.framePacks) {
      for(const {file,lengths} of manifest.framePacks) {
        const pack=await fetchBlob(file);
        if(lengths.reduce((sum,n)=>sum+n,0)!==pack.size)throw Error('Incomplete background pack: '+file);
        let offset=0;
        for(const length of lengths) {
          await decode(pack.slice(offset,offset+length,'image/jpeg'));
          offset+=length;
        }
      }
    } else {
      // Existing prepared runtimes serve individual JPEGs.
      for(const name of manifest.frames)await decode(name===manifest.original ? previewBlob : await fetchBlob(name));
    }
  } catch(error) {
    if(signal.aborted || images.length<playable) {
      player.dispose();
      throw error;
    }
    // A failed optional depth download still leaves a complete playable loop.
  }
  return player;
}

export function backgroundPlayer(manifest,images,state=initialPlayback()) {
  const select=backgroundAnimation(manifest.animation,Math.random,state);
  if(state.current===null)select(0);
  return {
    // Worker handoff transfers ownership of the decoded frames without copying pixels.
    frames: {manifest,images,state},
    advance(seconds) {
      const current=state.current;
      return select(seconds,images.length).current!==current;
    },
    draw(context,width,height,dimming=0) {
      const aspect=manifest.width/manifest.height;
      const w=Math.max(width,height*aspect), h=w/aspect;
      const x=Math.floor(width/2)-w/2, y=Math.floor(height/2)-h/2;
      const {offsetX,offsetY}=manifest.animation;
      context.save();
      context.globalAlpha=1;
      context.drawImage(images[state.previous],x-offsetX,y-offsetY,w,h);
      context.globalAlpha=.5;
      context.drawImage(images[state.current],x+offsetX,y+offsetY,w,h);
      // Dimming belongs to workspace preferences, after both animation layers.
      // Loading retains the undimmed native menu background by default.
      if(dimming>0) {
        context.globalAlpha=Math.min(.8,dimming);
        context.fillStyle='#000';
        context.fillRect(0,0,width,height);
      }
      context.restore();
    },
    dispose() {for(const image of images) image.close();},
  };
}
