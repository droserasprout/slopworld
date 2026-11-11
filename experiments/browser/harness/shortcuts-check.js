;(async()=>{
  const root='/home/droserasprout/git/slopworld';
  const command="python3 -u '"+root+"/experiments/browser/harness/benchmark-fixture.py'";
  const managed=window.__managedProbe,canvas=document.querySelector('canvas:not(#loading-screen)');
  const state=()=>JSON.parse(managed.WorkspaceState());
  const readers=action=>JSON.parse(managed.WorkspaceShortcutReaders(action,root+'/AGENTS.md',root+'/notes/README.md'));
  const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));
  const results=[];
  const before=state();
  let fixture;
  try {
    if (!managed.WorkspaceShortcutReaders) throw new Error('Refresh runtime for native shortcut reader fixtures');
    fixture=await window.__benchmark.prepare(command);
    const press=async(key,expectedSession,expectedReader=null)=>{
      const currentReader=readers('state');
      if (!fixture.created.includes(state().session) ||
          (currentReader.current && !currentReader.created.includes(currentReader.current)))
        throw new Error('Refusing shortcut input outside owned fixtures');
      canvas.focus();
      canvas.dispatchEvent(new KeyboardEvent('keydown',{key,code:'Key'+key.toUpperCase(),altKey:true,bubbles:true}));
      canvas.dispatchEvent(new KeyboardEvent('keyup',{key,code:'Key'+key.toUpperCase(),altKey:true,bubbles:true}));
      const deadline=performance.now()+5000;
      let after,reader;
      const originalSession=state().session;
      const matches=()=> (expectedSession===null ? after.session!==originalSession && !!after.session : after.session===expectedSession) && reader===expectedReader;
      do {
        await pause(30); after=state(); reader=readers('state').current;
        if (matches()) break;
      } while (performance.now()<deadline);
      const result={key:'Alt+'+key.toUpperCase(),session:after.session,reader,
        expectedSession:expectedSession??'adjacent session',expectedReader,pass:matches()};
      results.push(result);
      if (!result.pass) throw new Error('Shortcut failed: '+JSON.stringify({...result,readers:readers('state'),errors:after.errors.filter(error=>!before.errors.includes(error)),view:after.doors.view}));
    };
    await press('x',null);
    // Native ordering includes other project/session groups. Return through the
    // native API before every key so navigation never types into an existing pane.
    await window.__benchmark.select(fixture.created[0]);
    await press('z',null);
    await window.__benchmark.select(fixture.created[0]);
    const native=readers('prepare');
    if (native.created.length!==2) throw new Error('Expected two pinned native readers');
    await window.__benchmark.select(fixture.created[0]);
    // Crossing groups restores the reader cursor; subsequent keys cycle readers.
    await press('s',fixture.created[0],native.created[1]);
    await press('a',fixture.created[0],native.created[0]);
    await press('s',fixture.created[0],native.created[1]);
    await press('z',fixture.created[0]);
    await press('x',null);
    const after=state();
    const errors=after.errors.filter(error=>!before.errors.includes(error));
    if (errors.length) throw new Error('New managed errors: '+JSON.stringify(errors));
    if (after.game.hasGame || after.game.hasWorld || after.game.maps!==null || after.game.ticks!==0)
      throw new Error('Shortcut check created a colony');
    return {fixture,results,errors,game:after.game,pass:true};
  } finally {
    try { if (managed.WorkspaceShortcutReaders) readers('cleanup'); }
    finally { await window.__benchmark.cleanup(); }
  }
})()
