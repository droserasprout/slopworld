// Keep fixture ownership until the caller captures its screenshot and calls cleanup.
(async()=>{
  const b=window.__benchmark,m=window.__managedProbe,canvas=document.querySelector('canvas');
  try {
    await b.prepare(window.__clipboardFixtureCommand);
    if(!b.created.includes(JSON.parse(m.WorkspaceState()).session))throw Error('Not a fixture');
    for(const key of 'edges')canvas.dispatchEvent(new KeyboardEvent('keydown',{key,bubbles:true}));
    for(const type of ['keydown','keyup'])canvas.dispatchEvent(new KeyboardEvent(type,{key:'Enter',code:'Enter',bubbles:true}));
    const deadline=performance.now()+15000;
    while(!m.WorkspaceTerminalContains(b.created[0],'BOTTOM_gyp_Mj')) {
      if(performance.now()>deadline)throw Error('Bottom row not delivered');
      await new Promise(r=>setTimeout(r,50));
    }
    const state=JSON.parse(m.WorkspaceState()),g=state.terminalGeometry;
    if(g.rows*g.cellHeight>g.bounds[3])throw Error('Terminal admits clipped rows');
    canvas.dispatchEvent(new PointerEvent('pointermove',{clientX:innerWidth-2,clientY:innerHeight/2}));
    await new Promise(requestAnimationFrame);
    const overlay=Array.from(document.querySelectorAll('canvas')).find(c=>c.getAttribute('aria-hidden')==='true');
    if(canvas.style.cursor!=='none' || !overlay || overlay.style.display!=='block')throw Error('Edge cursor fallback');
    if(Math.abs(overlay.getBoundingClientRect().width*devicePixelRatio-overlay.width)>.1)throw Error('Cursor density mismatch');
    return {geometry:g,cursorPhysicalWidth:overlay.width,cursorCssWidth:overlay.getBoundingClientRect().width,density:devicePixelRatio,fixture:b.created};
  } catch(error) {await b.cleanup();throw error;}
})()
