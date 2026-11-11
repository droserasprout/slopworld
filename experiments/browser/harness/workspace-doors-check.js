// Native menu ownership and colony-free presence; no music selection is invoked.
(async () => {
  const managed=window.__managedProbe, canvas=document.querySelector('canvas');
  const state=()=>JSON.parse(managed.WorkspaceState());
  const initial=state();
  if(initial.game.hasGame || initial.game.programState!=='Entry')throw new Error('Expected colony-free Entry');
  const previous=managed.WorkspaceObserveLabels(true);
  const wait=()=>new Promise(resolve=>setTimeout(resolve,500));
  const observations=()=>JSON.parse(managed.WorkspaceLabels());
  const labels=()=>observations().map(l=>l.text);
  const pressLabel=async(text)=>{
    const label=observations().find(l=>l.text===text);
    if(!label)throw new Error('Missing clickable label: '+text);
    const r=canvas.getBoundingClientRect();
    for(const type of ['pointerdown','pointerup']){
      canvas.dispatchEvent(new PointerEvent(type,{button:0,clientX:r.left+(label.x+label.width/2)*r.width/canvas.width,clientY:r.top+(label.y+label.height/2)*r.height/canvas.height}));
      await new Promise(resolve=>setTimeout(resolve,100));
    }
    await wait();
  };
  const bubble=()=>{
    const width=Math.min(320,state().uiWidth-16)-16,scale=state().uiScale;
    return observations().findLast(l=>Math.abs(l.width/scale-width)<1);
  };
  const requireBubble=()=>{
    const current=bubble();
    if(!current || !current.text || labels().includes('Hint') || labels().includes('OK'))
      throw new Error('Expected a framework bubble above the pane, without a modal: '+JSON.stringify(observations().slice(-8)));
    if(current.x<0||current.y<0||current.x+current.width>canvas.width+1||current.y+current.height>canvas.height+1)
      throw new Error('Bubble text escaped the viewport');
    return current.text;
  };
  const key=async()=>{
    for(const type of ['keydown','keyup']){
      canvas.dispatchEvent(new KeyboardEvent(type,{key:'Escape',code:'Escape',bubbles:true}));await wait();
    }
  };
  const click=async(name)=>{
    const door=state().doors[name];
    if(!door.visible)throw new Error('Missing native door: '+name);
    const [x,y,w,h]=door.rect, r=canvas.getBoundingClientRect(), scale=state().uiScale;
    for(const type of ['pointerdown','pointerup']){
      canvas.dispatchEvent(new PointerEvent(type,{button:0,clientX:r.left+(x+w/2)*scale*r.width/canvas.width,clientY:r.top+(y+h/2)*scale*r.height/canvas.height}));await wait();
    }
  };
  try{
    await wait();
    if(labels().some(text=>text==='Hint'||text.startsWith('OST unavailable')))await key();
    await click('core');
    const core=labels();
    for(const label of ['Hint','Advice','Keyboard shortcuts','New looks (requires colony)','Next planet (requires colony)'])
      if(!core.includes(label))throw new Error('Missing native Core menu label: '+label);
    await pressLabel('Advice');
    const advice=requireBubble();
    await new Promise(resolve=>setTimeout(resolve,3300));
    if(observations().some(l=>l.text===advice))throw new Error('Advice did not expire');
    await click('core');await pressLabel('Hint');
    const hint=requireBubble();
    await new Promise(resolve=>setTimeout(resolve,3300));
    requireBubble(); // Every hint request includes at least a second three-second bubble.
    await new Promise(resolve=>setTimeout(resolve,10000));
    if(bubble())throw new Error('Hint sequence did not expire');
    await click('jukebox');
    const jukebox=labels();
    if(jukebox.some(text=>text.startsWith('OST unavailable')))throw new Error('Browser OST transport is disabled');
    for(const label of ['Settings'])
      if(!jukebox.some(text=>text.startsWith(label)))throw new Error('Missing native Jukebox menu label: '+label+'; rendered: '+JSON.stringify(jukebox));
    await key();
    const final=state();
    if(final.game.hasGame)throw new Error('Door created a colony');
    return {core,jukebox,bubbles:{advice,hint,adviceExpired:true,hintsSequenced:true,hintsExpired:true},doors:final.doors,game:final.game,playbackTested:false};
  }finally{
    if(labels().some(text=>text==='Hint'||text.startsWith('OST unavailable')))await key();
    managed.WorkspaceObserveLabels(previous);
  }
})()
