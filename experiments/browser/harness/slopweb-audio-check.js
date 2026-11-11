// An invocation-owned browser with default OST selected. CDP supplies user
// activation; the event targets window, never a terminal or native control.
(async()=>{
  const original=HTMLMediaElement.prototype.play;
  let audio;
  HTMLMediaElement.prototype.play=function(...args){audio=this;return original.apply(this,args);};
  try {
    window.dispatchEvent(new PointerEvent('pointerdown'));
    if(!audio)throw Error('Expected blocked OST awaiting user activation');
    const deadline=Date.now()+15000;
    while(audio.paused || audio.currentTime===0){
      if(Date.now()>deadline)throw Error('OST did not start after activation');
      await new Promise(resolve=>setTimeout(resolve,100));
    }
    return {playing:!audio.paused,time:audio.currentTime,duration:audio.duration,volume:audio.volume,
      track:JSON.parse(window.__managedProbe.WorkspaceAudio()).track};
  } finally {HTMLMediaElement.prototype.play=original;}
})()
