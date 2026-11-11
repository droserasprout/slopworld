// Invocation-owned Entry browser only. Local selection changes never send keys
// to a terminal process or stop/restart a session; restore presentation afterward.
(async()=>{
  const p=window.__managedProbe,c=document.querySelector('canvas');
  const state=()=>JSON.parse(p.WorkspaceState());
  const pause=()=>new Promise(resolve=>setTimeout(resolve,400));
  const original=state();
  if(original.game.programState!=='Entry')throw Error('Expected Entry workspace');
  p.WorkspaceSavePreferences();
  const storage='slopworld.presentation.v1',saved=localStorage.getItem(storage);
  const tab=JSON.parse(saved).preferences.sidebarTab;
  const results=[];
  const toggle=async()=>{
    for(const type of ['keydown','keyup']){
      c.dispatchEvent(new KeyboardEvent(type,{key:'F12',code:'F12',bubbles:true}));
      await pause();
    }
    if(window.__workspace.status!=='running')throw Error(window.__workspace.error);
  };
  try {
    for(const next of ['files','search','git','library','tasks','agents']){
      p.WorkspaceAppearance(JSON.stringify({sidebarTab:next}));await pause();
      const order=JSON.parse(p.WorkspaceSessionOrder());
      if(!order.length)throw Error('No eligible ordinary agents for '+next);
      const stopped=p.WorkspaceObserveStoppedSession();await pause();
      if(!state().windowOpen || state().session!==stopped)throw Error('Stopped pane did not open');
      await toggle();
      if(state().windowOpen)throw Error('F12 did not close stopped pane');
      await toggle();
      const after=state();
      if(!after.windowOpen || after.session===stopped)throw Error('F12 did not find a live session');
      // Native UI clicks have no resolved sound in Entry; OST uses a separate
      // browser transport. Keep all managed exceptions fatal to this check.
      const errors=after.errors.filter(error=>!original.errors.includes(error)
        && error!=='Cannot play Click (subSound Click_0_: No resolved grains.');
      if(errors.length)throw Error(JSON.stringify(errors));
      results.push({tab:next,candidates:order.length,reopened:after.session});
    }
    return {entry:true,stoppedPane:true,fallbackShortcut:true,results};
  } finally {
    p.WorkspaceAppearance(JSON.stringify({sidebarTab:tab}));
    if(window.__workspace.status==='running')p.WorkspaceSelect(original.session);
    p.WorkspaceSavePreferences();localStorage.setItem(storage,saved);
  }
})()
