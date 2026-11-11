// Native menu ownership and colony-free presence; no music selection is invoked.
(async () => {
  const managed=window.__managedProbe, canvas=document.querySelector('canvas');
  const state=()=>JSON.parse(managed.WorkspaceState());
  const initial=state();
  if(initial.game.hasGame || initial.game.programState!=='Entry')throw new Error('Expected colony-free Entry');
  const previous=managed.WorkspaceObserveLabels(true);
  const wait=()=>new Promise(resolve=>setTimeout(resolve,500));
  const labels=()=>JSON.parse(managed.WorkspaceLabels()).map(l=>l.text);
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
    await key();await click('jukebox');
    const jukebox=labels();
    for(const label of ['OST unavailable','Settings'])
      if(!jukebox.some(text=>text.startsWith(label)))throw new Error('Missing native Jukebox menu label: '+label+'; rendered: '+JSON.stringify(jukebox));
    await key();
    const final=state();
    if(final.game.hasGame)throw new Error('Door created a colony');
    return {core,jukebox,doors:final.doors,game:final.game,playbackTested:false};
  }finally{
    if(labels().some(text=>text==='Hint'||text.startsWith('OST unavailable')))await key();
    managed.WorkspaceObserveLabels(previous);
  }
})()
