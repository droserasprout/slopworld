// Fixed host workload in disposable sessions. DOM input is guarded by exact
// invocation-owned session names. Existing sessions are only restored/read.
(async () => {
  const managed = window.__managedProbe, canvas = document.querySelector('canvas');
  const {renderGui} = await import('./gui.js');
  if (window.__workspace?.status !== 'running') throw new Error('Workspace is not running');
  if (!window.__workspace.setProfiling) throw new Error('Refresh the browser runtime for benchmark profiling');
  if (window.__benchmark) throw new Error('An earlier benchmark still owns sessions');
  const initial = JSON.parse(managed.WorkspaceState()).session;
  const created = [], samples = [];
  const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
  const until = async (predicate, description) => {
    const deadline = performance.now() + 20000;
    while (!predicate()) {
      if (window.__workspace.status !== 'running') throw new Error('Workspace stopped');
      if (performance.now() > deadline) throw new Error('Timeout: ' + description);
      await pause(20);
    }
  };
  const integer = value => {
    const bytes = [];
    do { let byte = value & 127; value >>>= 7; if (value) byte |= 128; bytes.push(byte); } while (value);
    return bytes;
  };
  const string = (field, value) => {
    const bytes = new TextEncoder().encode(value);
    return [...integer(field * 8 + 2), ...integer(bytes.length), ...bytes];
  };
  const fields = bytes => {
    let offset = 0; const result = new Map();
    const number = () => {
      let value = 0n, shift = 0n;
      for (let i = 0; i < 10; i++) {
        if (offset >= bytes.length) throw new Error('Truncated protobuf');
        const byte = bytes[offset++]; value |= BigInt(byte & 127) << shift;
        if (!(byte & 128)) return Number(value);
        shift += 7n;
      }
      throw new Error('Invalid protobuf integer');
    };
    while (offset < bytes.length) {
      const tag = number(), wire = tag & 7; let value;
      if (wire === 0) value = number();
      else if (wire === 2) { const size = number(); value = bytes.slice(offset, offset + size); offset += size; }
      else if (wire === 1) offset += 8;
      else if (wire === 5) offset += 4;
      else throw new Error('Invalid protobuf wire type');
      if (offset > bytes.length) throw new Error('Truncated protobuf value');
      result.set(tag >>> 3, value);
    }
    return result;
  };
  const state = () => JSON.parse(managed.WorkspaceState());
  const owned = () => {
    const name = state().session;
    if (!created.includes(name)) throw new Error('Refusing benchmark input into an existing session');
    return name;
  };
  const send = value => {
    owned(); canvas.focus();
    for (const key of value) canvas.dispatchEvent(new KeyboardEvent('keydown', {key, bubbles: true}));
    canvas.dispatchEvent(new KeyboardEvent('keydown', {key:'Enter', code:'Enter', bubbles:true}));
    canvas.dispatchEvent(new KeyboardEvent('keyup', {key:'Enter', code:'Enter', bubbles:true}));
  };
  const select = async name => {
    let commands;
    await until(() => {
      try { commands = JSON.parse(managed.WorkspaceSelect(name)); renderGui(canvas,commands); return true; }
      catch (error) {
        if (String(error).includes('Requested workspace session is not live')) return false;
        throw error;
      }
    }, 'session catalog admission');
    await until(() => state().session === name && state().terminalDrawn, 'session attachment');
  };
  const output = marker => until(() => managed.WorkspaceTerminalContains(owned(), marker), marker);
  const api = async (path, options) => {
    const response = await fetch('/host/api/' + path, options);
    const bytes = new Uint8Array(await response.arrayBuffer());
    if (!response.ok) throw new Error(path + ': HTTP ' + response.status);
    return fields(bytes);
  };
  const cleanup = async () => {
    window.__workspace.setProfiling(false);
    const failures = [];
    if (initial) {
      try { await select(initial); }
      catch (error) { failures.push('Restore: ' + error); }
    }
    for (const name of created) {
      try {
        const existing=await fetch('/host/api/sessions/' + encodeURIComponent(name));
        const metadata=new Uint8Array(await existing.arrayBuffer());
        if(existing.status === 404)continue; // Ephemeral processes can exit during input checks.
        if(existing.ok && fields(metadata).get(27)!==1) {
          await api('sessions/' + encodeURIComponent(name) + '/stop', {method:'POST'});
          const stopped=await fetch('/host/api/sessions/' + encodeURIComponent(name));
          await stopped.arrayBuffer();
          if(stopped.status===404)continue;
        }
        await api('sessions/' + encodeURIComponent(name), {method:'DELETE'});
        const check=await fetch('/host/api/sessions/' + encodeURIComponent(name));
        await check.arrayBuffer();
        if (check.status !== 404) throw new Error('Deleted fixture remains visible: HTTP ' + check.status);
      }
      catch (error) { failures.push(String(error)); }
    }
    if (failures.length) throw new Error('Benchmark cleanup: ' + failures.join('; '));
    delete window.__benchmark;
  };
  window.__benchmark = {
    version: 1, created, cleanup, select,
    async prepareImage() {
      if(created.length || !initial)throw Error('Image fixture requires a source and no active fixtures');
      const body=new Uint8Array([...string(4,'shell'),...string(5,'codex'),
        ...string(9,'slopweb-image-'+crypto.randomUUID().slice(0,8)),...string(14,initial),80,1]);
      const result=await api('run',{method:'POST',headers:{'Content-Type':'application/x-protobuf'},body});
      const name=new TextDecoder().decode(result.get(2));
      if(!name)throw Error('Image fixture launch returned no session');
      created.push(name);
      const metadata=await api('sessions/'+encodeURIComponent(name));
      const launch=fields(metadata.get(45));
      if(metadata.get(26)!==1 || metadata.get(27)===1 || new TextDecoder().decode(launch.get(2))!=='codex')
        throw Error('Image fixture must be a temporary sandboxed Codex agent');
      await select(name);
      await until(()=>managed.WorkspaceTerminalContains(name,'Ask Codex'), 'Codex composer ready');
      await pause(2000); // The initial composer can precede model/session initialization.
      return name;
    },
    async prepare(command) {
      if (created.length) throw new Error('Benchmark fixtures already exist');
      for (let index = 0; index < 2; index++) {
        const body = new Uint8Array([
          ...string(4, 'shell'), ...string(5, command),
          ...string(9, 'slopweb-bench-' + crypto.randomUUID().slice(0,8)),
          80, 1, 96, 1, 8, ...integer(120), 16, ...integer(40),
        ]);
        const result = await api('run', {method:'POST', headers:{'Content-Type':'application/x-protobuf'}, body});
        const name = new TextDecoder().decode(result.get(2));
        if (!name) throw new Error('Launch returned no session');
        created.push(name);
        const session = await api('sessions/' + encodeURIComponent(name));
        if (session.get(26) !== 1 || session.get(27) !== 1)
          throw new Error('Benchmark launch is not an ephemeral host session');
        await pause(300);
        await select(name); await output('BENCH_READY');
      }
      await select(created[0]); await pause(1500);
      return {created, fixture:'200 fixed lines; 120x40 initial host grid', game:state().game};
    },
    async run(phase) {
      samples.length = 0;
      window.__workspace.setProfiling(true);
      let running = true, last;
      const intervals = [], drawIntervals = [], scrollTrace = [];
      let lastDraw, lastFrame;
      const frame = now => {
        if (!running) return;
        if (last !== undefined) intervals.push(now-last); last=now;
        if(lastFrame!==window.__workspace.frames) {
          const drawn=window.__workspace.frameStarted;
          if(lastDraw!==undefined)drawIntervals.push(drawn-lastDraw);
          lastDraw=drawn;lastFrame=window.__workspace.frames;
        }
        requestAnimationFrame(frame);
      };
      requestAnimationFrame(frame);
      const started = performance.now();
      try {
        if (phase === 'idle') await pause(5000);
        else if (phase === 'output') { send('stream'); await output('BENCH_STREAM_DONE'); }
        else if (phase === 'typing') {
          for (let i=0; i<20; i++) {
            const marker = 'TYPE_' + i.toString().padStart(2,'0');
            const start = performance.now(); send('echo ' + marker);
            await output('ACK_' + marker); samples.push(performance.now()-start);
            await pause(100);
          }
        } else if (phase === 'scrolling') {
          owned(); const bounds=canvas.getBoundingClientRect();
          scrollTrace.push({step:-1, elapsedMs:performance.now()-started, ...state().terminalScroll});
          for (let i=0; i<40; i++) {
            canvas.dispatchEvent(new WheelEvent('wheel', {deltaY:i<20?-80:80,
              clientX:bounds.left+bounds.width*.75, clientY:bounds.top+bounds.height*.5, bubbles:true}));
            await pause(100);
            scrollTrace.push({step:i, elapsedMs:performance.now()-started, ...state().terminalScroll});
          }
          await pause(500);
          scrollTrace.push({step:40, elapsedMs:performance.now()-started, ...state().terminalScroll});
          if (!scrollTrace.some(sample=>sample.offset>0)) throw new Error('Wheel workload never entered native history');
        } else if (phase === 'switching') {
          for (let i=0; i<20; i++) {
            const start=performance.now(); await select(created[i%2]);
            samples.push(performance.now()-start); await pause(150);
          }
        } else throw new Error('Unknown workload phase');
        const game=state().game;
        if (game.hasGame || game.hasWorld || game.maps !== null || game.ticks !== 0)
          throw new Error('Benchmark created a colony');
        return {phase, durationMs:performance.now()-started, intervals, drawIntervals, latencyMs:[...samples],
          stages:[...window.__workspaceProfile], game, scrollTrace, terminalCache:state().terminalCache};
      } finally { running=false; window.__workspace.setProfiling(false); }
    },
  };
  return {installed:true};
})()
