// Run against a retained local workspace with a root-authorized host bridge.
// Only shells created by this invocation receive input or are removed. Existing
// coding agents are attached read-only. Observe the mod's actual receive buffer;
// never substitute backend API calls for the terminal's input/control path.
(async () => {
  const managed = window.__managedProbe, canvas = document.querySelector('canvas');
  if (window.__workspace?.status !== 'running') throw new Error('Workspace is not running');
  if (!managed.WorkspaceTerminalContains) throw new Error('Rebuild the workspace for terminal observations');
  const state = () => JSON.parse(managed.WorkspaceState());
  const initial = state();
  if(!managed.WorkspaceObserveLabels || !initial.sidebar)throw new Error('Refresh the runtime for native-font UI observations');
  if (!initial.windowOpen) throw new Error('Open the workspace with F12 before running this check');
  const originalClipboard=window.__checkClipboard ? await navigator.clipboard.readText() : null;
  const created = [], visited = [], markers = [];
  let attachedAgent;
  const previousLabels=managed.WorkspaceObserveLabels(true);
  const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
  const until = async (condition, description) => {
    const deadline = Date.now() + 20000;
    do {
      if (window.__workspace.status !== 'running') throw new Error('Workspace stopped: ' + description);
      if (condition()) return;
      await wait(100);
    } while (Date.now() < deadline);
    throw new Error('Timed out: ' + description);
  };
  const labels = () => {
    const scale=state().uiScale;
    return JSON.parse(managed.WorkspaceLabels()).map(l=>({...l,
      x:l.x+Math.min(l.width/2,5*scale),y:l.y+l.height/2}));
  };
  const uiBox = name => {
    const current=state(),rect=current.sidebar[name];
    return rect.map(value=>value*current.uiScale);
  };
  const point = (x,y) => {
    const r=canvas.getBoundingClientRect();
    return {clientX:r.left+x*r.width/canvas.width,clientY:r.top+y*r.height/canvas.height};
  };
  const click = async (x,y,button=0) => {
    for (const type of ['pointerdown','pointerup']) {
      canvas.dispatchEvent(new PointerEvent(type,{button,...point(x,y)}));
      await wait(120);
    }
  };
  const wheel = async deltaY => {
    const [x,y,w,h]=uiBox('body');
    canvas.dispatchEvent(new WheelEvent('wheel',{deltaY,...point(x+w/2,y+h/2),bubbles:true}));
    await wait(200);
  };
  const select = async session => {
    await wheel(-10000);
    for (let attempt=0;attempt<30;attempt++) {
      // Host titles can be identical paths or change with the foreground
      // process. Target the native row's identity and clipped scroll geometry.
      const row=JSON.parse(managed.WorkspaceSidebarRows()).find(r=>r.session===session.name);
      if (row?.rect?.[2]>0 && row.rect[3]>4) {
        const [x,y,w,h]=row.rect,scale=state().uiScale;
        await click((x+w/2)*scale,(y+h/2)*scale);
        await until(()=>state().session===session.name&&state().terminalDrawn,'select '+session.name);
        visited.push(session.name); return;
      }
      await wheel(350);
    }
    throw new Error('Sidebar row not found: '+session.name);
  };
  // Decode only catalog identity/runtime metadata; never report commands, args,
  // environment, configuration, or terminal contents from existing sessions.
  const fields = bytes => {
    let offset=0; const result=new Map();
    const integer=()=>{let n=0n,shift=0n;for(let i=0;i<10;i++){if(offset>=bytes.length)throw new Error('Truncated protobuf');const b=bytes[offset++];n|=BigInt(b&127)<<shift;if(!(b&128))return Number(n);shift+=7n;}throw new Error('Invalid varint');};
    while(offset<bytes.length) {
      const tag=integer(),number=tag>>>3,wire=tag&7;let value;
      if(wire===0)value=integer();
      else if(wire===2){const size=integer();if(size>bytes.length-offset)throw new Error('Invalid protobuf length');value=bytes.slice(offset,offset+size);offset+=size;}
      else if(wire===1)offset+=8;
      else if(wire===5)offset+=4;
      else throw new Error('Unknown protobuf wire type');
      if(offset>bytes.length)throw new Error('Truncated protobuf');
      if(!result.has(number))result.set(number,[]);result.get(number).push(value);
    }
    return result;
  };
  const catalog = async () => {
    const response=await fetch('/host/api/sessions');
    if(!response.ok)throw new Error('Sessions HTTP '+response.status);
    const root=fields(new Uint8Array(await response.arrayBuffer()));
    const decode=(f,n)=>new TextDecoder().decode(f.get(n)?.[0]);
    return (root.get(1)??[]).map(bytes=>{
      const s=fields(bytes),runtime=fields(s.get(44)?.[0]??new Uint8Array()),launch=fields(s.get(45)?.[0]??new Uint8Array());
      return {name:decode(s,1),label:decode(s,2),project:decode(s,3),ephemeral:s.get(26)?.[0]===1,host:s.get(27)?.[0]===1,alive:runtime.get(3)?.[0]===1,agent:decode(launch,6),cols:runtime.get(4)?.[0],rows:runtime.get(5)?.[0]};
    });
  };
  const type = text => {
    if(!created.some(s=>s.name===state().session))throw new Error('Refusing input into an existing session');
    canvas.focus();
    for(const key of text)canvas.dispatchEvent(new KeyboardEvent('keydown',{key,bubbles:true}));
  };
  const key = key => {
    if(!created.some(s=>s.name===state().session))throw new Error('Refusing input into an existing session');
    for(const event of ['keydown','keyup'])canvas.dispatchEvent(new KeyboardEvent(event,{key,code:key,bubbles:true}));
  };
  let before;
  try {
    const access=await fetch('/host/api/templates'); await access.arrayBuffer();
    if(!access.ok)throw new Error('This check needs a root-authorized host bridge');
    before=await catalog();
    const agent=before.find(s=>s.alive&&!s.host&&s.agent);
    if(!agent)throw new Error('An existing live coding agent is required for the read-only attach check');
    await until(()=>labels().length>0,'first observed GUI frame');
    for(let index=0;index<2;index++) {
      if(!labels().some(l=>l.text==='Host shell')) {
        const [x,y,w,h]=uiBox('add');await click(x+w/2,y+h/2);
      }
      await until(()=>labels().some(l=>l.text==='Host shell'),'Host shell menu');
      const host=labels().find(l=>l.text==='Host shell');
      canvas.dispatchEvent(new PointerEvent('pointermove',{...point(host.x,host.y)}));
      await until(()=>labels().some(l=>l.text==='~'),'Host shell submenu');
      const home=labels().find(l=>l.text==='~');
      await click(home.x,home.y);
      await until(()=>state().session&&!before.some(s=>s.name===state().session)&&!created.some(s=>s.name===state().session),'new disposable shell');
      const selected=state().session;
      const session=(await catalog()).find(s=>s.name===selected);
      if(!session?.host||!session.ephemeral||session.project||!session.alive)
        throw new Error('Created session is not an unscoped temporary host shell');
      created.push(session);
      await until(()=>state().terminalDrawn,'new terminal drawing');
      // Splitting the marker means command echo cannot satisfy the output check.
      const nonce=crypto.randomUUID().replaceAll('-','').slice(0,10);
      type(`printf 'BROWSER_%s_%s_END\\n' '${nonce}' 'OKX`);
      key('Backspace'); type("'"); key('Enter');
      const marker=`BROWSER_${nonce}_OK_END`;
      await until(()=>managed.WorkspaceTerminalContains(session.name,marker),'executed terminal output');
      markers.push(marker);
      if (window.__checkClipboard && index===0) {
        await navigator.clipboard.writeText(`printf 'PASTE_%s_🧪\\n' '${nonce}'`);
        await window.__trustedPaste();
        key('Enter');
        const pasted=`PASTE_${nonce}_🧪`;
        await until(()=>managed.WorkspaceTerminalContains(session.name,pasted),'browser paste output');
        await click(600,300,2);
        await until(()=>labels().some(l=>l.text==='Select all'),'terminal Select all menu');
        const item=labels().find(l=>l.text==='Select all');
        await click(item.x,item.y);
        await wait(300);
        const copied=await navigator.clipboard.readText();
        if(!copied.includes(marker)||!copied.includes(pasted))throw new Error('Native selection did not reach browser clipboard');
      }
      if(index===0) {
        // A second real client requests a different tmux grid. The browser must
        // not interpret its frames as lost acknowledgments and fight it forever.
        const socket=new WebSocket(location.origin.replace('http','ws')+'/host/ws','slopworld.protobuf.v2');
        try {
          await new Promise((resolve,reject)=>{socket.onopen=resolve;socket.onerror=reject;});
          const integer=n=>{const bytes=[];do{let b=n&127;n>>>=7;if(n)b|=128;bytes.push(b);}while(n);return bytes;};
          const nameBytes=new TextEncoder().encode(session.name);
          const resize=[10,...integer(nameBytes.length),...nameBytes,16,97,24,28];
          socket.send(new Uint8Array([42,...integer(resize.length),...resize]));
          await wait(1000);
          for(let sample=0;sample<8;sample++) {
            const actual=(await catalog()).find(s=>s.name===session.name);
            if(actual?.cols!==97||actual?.rows!==28)throw new Error('Browser fought a second client resize: '+JSON.stringify({cols:actual?.cols,rows:actual?.rows}));
            await wait(500);
          }
        } finally {socket.close();}
      }
    }
    for(const index of [0,1,0]) {
      await select(created[index]);
      await until(()=>managed.WorkspaceTerminalContains(created[index].name,markers[index]),'output after switching');
    }
    await select(agent); attachedAgent=agent.name;
    const later=state();
    if(later.game.hasGame||later.game.hasWorld||later.game.maps!==null||later.game.ticks!==0||later.game.programState!=='Entry')
      throw new Error('Workspace created a colony');
    if(later.frame<=initial.frame||later.elapsed<=initial.elapsed)throw new Error('UI clock did not advance');
    return {created:created.map(s=>s.name),typedOutputVerified:true,backspaceAndEnter:true,clipboard:!!window.__checkClipboard,sharedTerminalSizeStable:true,visited,attachedAgent,agentReadOnly:true,uiClockAdvanced:true,game:later.game,cleanup:'completed before return'};
  } finally {
    const failures=[];
    try {if(originalClipboard!==null)await navigator.clipboard.writeText(originalClipboard);}
    catch(error){failures.push('Clipboard restore: '+error);}
    try {managed.WorkspaceObserveLabels(previousLabels);}
    catch(error){failures.push('Observation restore: '+error);}
    try {if(initial.session)managed.WorkspaceSelect(initial.session);}
    catch(error){failures.push('Selection restore: '+error);}
    for(const session of created) {
      try {
        const response=await fetch('/host/api/sessions/'+encodeURIComponent(session.name),{method:'DELETE'});
        await response.arrayBuffer();
        if(!response.ok)failures.push(session.name+': HTTP '+response.status);
      } catch(error){failures.push(session.name+': '+error);}
    }
    try {
      const remaining=await catalog();
      for(const session of created)if(remaining.some(s=>s.name===session.name))failures.push(session.name+': still present');
    } catch(error){failures.push('Cleanup verification: '+error);}
    if(failures.length)throw new Error('Workflow cleanup failed: '+failures.join(', '));
  }
})()
