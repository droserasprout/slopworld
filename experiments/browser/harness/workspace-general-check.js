// Inspect the actual native General page, preserving the Entry workspace.
(async()=>{
  const managed=window.__managedProbe,canvas=document.querySelector('canvas:not(#loading-screen)');
  const previous=managed.WorkspaceObserveLabels(true);
  const wait=()=>new Promise(resolve=>setTimeout(resolve,500));
  const labels=()=>JSON.parse(managed.WorkspaceLabels());
  const click=async(x,y)=>{
    const r=canvas.getBoundingClientRect();
    for(const type of ['pointermove','pointerdown','pointerup']){
      canvas.dispatchEvent(new PointerEvent(type,{button:0,clientX:r.left+x*r.width/canvas.width,clientY:r.top+y*r.height/canvas.height,bubbles:true}));await wait();
    }
  };
  const label=title=>{const found=labels().find(l=>l.text===title);if(!found)throw Error('Missing label '+title);return found;};
  try{
    await wait();
    const state=JSON.parse(managed.WorkspaceState()),[x,y,w,h]=state.doors.config.rect;
    await click((x+w/2)*state.uiScale,(y+h/2)*state.uiScale);
    const general=label('General');await click(general.x+5,general.y+general.height/2);
    const identity=labels().find(l=>l.text.startsWith('Client: slopweb '));
    if(!identity)throw Error('General page lacks slopweb identity');
    const eco=label('Eco mode');
    const pixels=()=>Array.from(canvas.getContext('2d').getImageData(Math.floor(eco.x-32*state.uiScale),Math.floor(eco.y),Math.ceil(eco.width+32*state.uiScale),Math.ceil(eco.height)).data);
    const before=pixels();
    await click(eco.x+5,eco.y+eco.height/2);
    if(JSON.stringify(before)!==JSON.stringify(pixels()))throw Error('Locked Eco checkbox changed on click');
    const after=JSON.parse(managed.WorkspaceState());
    if(after.game.hasGame||after.game.hasWorld||after.game.ticks!==0)throw Error('Entry invariant changed');
    return {client:identity.text,ecoClickUnchanged:true,game:after.game};
  }finally{managed.WorkspaceObserveLabels(previous);}
})()
