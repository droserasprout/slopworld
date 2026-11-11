// Click native TopBar geometry through DOM input, without typing into a session.
(async()=>{
  if(window.__workspace?.status!=='running')throw new Error('Workspace not running');
  const managed=window.__managedProbe,canvas=document.querySelector('canvas:not(#loading-screen)');
  const state=()=>JSON.parse(managed.WorkspaceState());
  const initial=state();
  if(!initial.doors.planner.visible)throw new Error('Native Planner door is absent');
  if(initial.doors.view)throw new Error('Run this check from the terminal view');
  const click=async()=>{
    const snapshot=state(),[x,y,w,h]=snapshot.doors.planner.rect,r=canvas.getBoundingClientRect();
    const at={clientX:r.left+(x+w/2)*r.width/snapshot.uiWidth,clientY:r.top+(y+h/2)*r.height/snapshot.uiHeight,button:0,bubbles:true};
    for(const type of ['pointermove','pointerdown','pointerup']) {
      canvas.dispatchEvent(new PointerEvent(type,at));
      await new Promise(resolve=>setTimeout(resolve,250));
    }
  };
  let opened=false;
  try {
    await click();
    const snapshot=state();opened=snapshot.doors.view==='PlannerView';
    if(!opened)throw new Error('Planner door did not open native PlannerView');
    if(snapshot.game.hasGame||snapshot.game.hasWorld||snapshot.game.ticks)throw new Error('Colony boundary changed');
    return {plannerOpened:true,configVisible:snapshot.doors.config.visible,doors:snapshot.doors,game:snapshot.game};
  } finally {
    if(opened) {
      await click();
      if(state().doors.view!==null || state().session!==initial.session)
        throw new Error('Planner toggle did not restore the original terminal');
    }
  }
})()
