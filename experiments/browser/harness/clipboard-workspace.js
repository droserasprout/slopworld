// Run after benchmark-workload.js; input stays in invocation-owned disposable shells.
(async()=>{
  const benchmark=window.__benchmark, managed=window.__managedProbe;
  const canvas=document.querySelector('canvas');
  const report=window.__clipboardReport={stage:"prepare"};
  let saved;
  const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));
  const until=async predicate=>{const end=performance.now()+20000;while(!await predicate()){
    if(performance.now()>end)throw Error('Clipboard delivery timed out');await pause(50);
  }};
  try {
    const before=await navigator.clipboard.read();
    saved=await Promise.all(before.filter(item=>item.types.length).map(async item=>new ClipboardItem(Object.fromEntries(
      await Promise.all(item.types.map(async type=>[type,await item.getType(type)]))))));
    const fixture=await benchmark.prepare(window.__clipboardFixtureCommand);
    const session=fixture.created[0];
    const marker='CLIPBOARD_'+crypto.randomUUID().slice(0,8)+'_🧪';
    report.stage='paste';
    await navigator.clipboard.writeText('echo '+marker);
    await window.__trustedPaste();
    canvas.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',code:'Enter',bubbles:true}));
    canvas.dispatchEvent(new KeyboardEvent('keyup',{key:'Enter',code:'Enter',bubbles:true}));
    await until(()=>managed.WorkspaceTerminalContains(session,'ACK_'+marker));
    report.stage='select';
    const state=JSON.parse(managed.WorkspaceState());
    const [x,y,w,h]=state.terminalGeometry.bounds;
    const rect=canvas.getBoundingClientRect();
    const point=(px,py)=>({clientX:rect.left+px*state.uiScale*rect.width/canvas.width,
      clientY:rect.top+py*state.uiScale*rect.height/canvas.height,button:0,bubbles:true});
    canvas.dispatchEvent(new PointerEvent('pointerdown',point(x+20,y+20)));
    canvas.dispatchEvent(new PointerEvent('pointermove',{...point(x+w-20,y+h-2),buttons:1}));
    canvas.dispatchEvent(new PointerEvent('pointerup',point(x+w-20,y+h-2)));
    await pause(300);
    report.stage='copy';
    window.__clipboardKey('copy');
    await until(async()=>(await navigator.clipboard.readText()).includes('ACK_'+marker));
    return {textPaste:true,unicode:true,selectionCopy:true,session};
  } finally {
    report.finalStage=report.stage;
    try { await benchmark.cleanup(); }
    finally { if(saved?.length)await navigator.clipboard.write(saved);else if(saved)await navigator.clipboard.writeText(''); }
  }
})()
