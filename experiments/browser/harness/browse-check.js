(async()=>{
  const managed=window.__managedProbe,canvas=document.querySelector('canvas:not(#loading-screen)');
  if(window.__workspace?.status!=='running')throw new Error('Workspace not running');
  const before=JSON.parse(managed.WorkspaceState());
  const catalogBefore=JSON.parse(managed.WorkspaceBrowseState());
  const r=canvas.getBoundingClientRect();
  const point={clientX:r.left+50*r.width/before.uiWidth,clientY:r.top+15*r.height/before.uiHeight,button:0,bubbles:true};
  for(const type of ['pointermove','pointerdown','pointerup']){
    canvas.dispatchEvent(new PointerEvent(type,point));
    await new Promise(resolve=>setTimeout(resolve,250));
  }
  await new Promise(resolve=>setTimeout(resolve,1500));
  const after=JSON.parse(managed.WorkspaceState());
  const catalogAfter=JSON.parse(managed.WorkspaceBrowseState());
  if(catalogAfter.online && catalogAfter.projects>0 && catalogAfter.catalogs===0)
    throw new Error('Projects exist but Files catalog is empty: '+JSON.stringify({catalogBefore,catalogAfter}));
  return {catalogBefore,catalogAfter,errors:after.errors.filter(error=>!before.errors.includes(error)),game:after.game,session:after.session};
})()
