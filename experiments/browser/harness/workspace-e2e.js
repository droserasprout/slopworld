// Run against a retained local workspace with a root-authorized host bridge.
// Only shells created by this invocation receive input or are removed. Existing
// coding agents are attached read-only. Observe the mod's actual receive buffer;
// never substitute backend API calls for the terminal's input/control path.
(async () => {
  const managed = window.__managedProbe, canvas = document.querySelector('canvas');
  if (window.__workspace?.status !== 'running') throw new Error('Workspace is not running');
  if (!managed.WorkspaceTerminalContains) throw new Error('Rebuild the workspace for terminal observations');
  const state = () => JSON.parse(managed.WorkspaceState());
  const initial = state(), original = managed.WorkspaceUpdate;
  if (!initial.windowOpen) throw new Error('Open the workspace with F12 before running this check');
  const created = [], visited = [], markers = [];
  let commands = [], attachedAgent;
  managed.WorkspaceUpdate = (...args) => {
    const value = original(...args); commands = JSON.parse(value).commands; return value;
  };
  const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
  const until = async (condition, description) => {
    const deadline = Date.now() + 8000;
    do {
      if (window.__workspace.status !== 'running') throw new Error('Workspace stopped: ' + description);
      if (condition()) return;
      await wait(100);
    } while (Date.now() < deadline);
    throw new Error('Timed out: ' + description);
  };
  const labels = () => {
    let matrix = new DOMMatrix(); const stack = [], result = [];
    for (const {kind,value:v} of commands) {
      if (kind === 'matrix') matrix = new DOMMatrix(v);
      else if (kind === 'group') {stack.push(matrix);matrix=matrix.translate(v.rect[0]-v.offset[0],v.rect[1]-v.offset[1]);}
      else if (kind === 'clip' || kind === 'clipScreen') stack.push(matrix);
      else if (kind === 'pop') matrix=stack.pop();
      else if (kind === 'text') {
        const p = new DOMPoint(v.rect[0]+5, v.rect[1]+10).matrixTransform(matrix);
        result.push({text:v.text,x:p.x,y:p.y});
      }
    }
    return result;
  };
  const point = (x,y) => {
    const r=canvas.getBoundingClientRect();
    return {clientX:r.left+x*r.width/canvas.width,clientY:r.top+y*r.height/canvas.height};
  };
  const click = async (x,y) => {
    for (const type of ['pointerdown','pointerup']) {
      canvas.dispatchEvent(new PointerEvent(type,{button:0,...point(x,y)}));
      await wait(120);
    }
  };
  const wheel = async deltaY => {
    canvas.dispatchEvent(new WheelEvent('wheel',{deltaY,...point(100,550),bubbles:true}));
    await wait(200);
  };
  const select = async session => {
    await wheel(-10000);
    for (let attempt=0;attempt<30;attempt++) {
      // Match the left-hand identity, not a right-hand command-preset label
      // such as "zsh" belonging to a different existing session.
      const row=labels().find(l=>[session.name,session.label].includes(l.text)&&l.x<80&&l.y>30&&l.y<680);
      if (row) {
        await click(row.x,row.y);
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
      return {name:decode(s,1),label:decode(s,2),project:decode(s,3),ephemeral:s.get(26)?.[0]===1,host:s.get(27)?.[0]===1,alive:runtime.get(3)?.[0]===1,agent:decode(launch,6)};
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
    await until(()=>commands.length>0,'first GUI frame');
    for(let index=0;index<2;index++) {
      await click(97,705);
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
    return {created:created.map(s=>s.name),typedOutputVerified:true,backspaceAndEnter:true,visited,attachedAgent,agentReadOnly:true,uiClockAdvanced:true,game:later.game,cleanup:'completed before return'};
  } finally {
    managed.WorkspaceUpdate=original;
    const failures=[];
    for(const session of created) {
      const response=await fetch('/host/api/sessions/'+encodeURIComponent(session.name),{method:'DELETE'});
      await response.arrayBuffer();
      if(!response.ok)failures.push(session.name+': HTTP '+response.status);
    }
    const remaining=await catalog();
    for(const session of created)if(remaining.some(s=>s.name===session.name))failures.push(session.name+': still present');
    if(failures.length)throw new Error('Test shell cleanup failed: '+failures.join(', '));
  }
})()
