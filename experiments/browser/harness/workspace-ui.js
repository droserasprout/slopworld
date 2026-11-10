// Evaluate in an already-loaded workspace. Exercise actual mod settings through
// DOM events, then restore the local preference and return to the terminal.
(async () => {
  const managed = window.__managedProbe, canvas = document.querySelector('canvas');
  if (window.__workspace?.status !== 'running') throw new Error('Workspace is not running');
  const original = managed.WorkspaceUpdate;
  const initial = JSON.parse(managed.WorkspaceState());
  let commands = [];
  managed.WorkspaceUpdate = (...args) => {
    const value = original(...args); commands = JSON.parse(value).commands; return value;
  };
  const wait = () => new Promise(resolve => setTimeout(resolve, 250));
  const labels = () => {
    let matrix = new DOMMatrix(); const stack = [], labels = [];
    for (const {kind,value:v} of commands) {
      if (kind === 'matrix') matrix = new DOMMatrix(v);
      else if (kind === 'group') {stack.push(matrix);matrix=matrix.translate(v.rect[0]-v.offset[0],v.rect[1]-v.offset[1]);}
      else if (kind === 'clip' || kind === 'clipScreen') stack.push(matrix);
      else if (kind === 'pop') matrix=stack.pop();
      else if (kind === 'text') {
        const p = new DOMPoint(v.rect[0]+5, v.rect[1]+10).matrixTransform(matrix);
        labels.push({text:v.text,x:p.x,y:p.y});
      }
    }
    return labels;
  };
  const click = async (x,y) => {
    const r=canvas.getBoundingClientRect();
    for (const type of ['pointerdown','pointerup']) {
      canvas.dispatchEvent(new PointerEvent(type,{button:0,clientX:r.left+x*r.width/canvas.width,clientY:r.top+y*r.height/canvas.height}));
      await wait();
    }
  };
  const label = text => {
    const result=labels().find(l=>l.text===text);
    if(!result)throw new Error('Missing actual settings label: '+text);
    return result;
  };
  const key = async (key,code=key) => {
    for (const type of ['keydown','keyup']) {
      canvas.dispatchEvent(new KeyboardEvent(type,{key,code,bubbles:true}));
      await wait();
    }
  };
  try {
    await wait();
    if (!labels().some(l=>l.text==='Appearance')) await click(1264,15);
    const keyboard=label('Keyboard'); await click(keyboard.x,keyboard.y);
    label('Open/close agent terminal'); label('F12');
    const bindingLabel=label('Duplicate selected agent');
    const bindingButton=()=>labels().find(l=>l.x>bindingLabel.x+300 && Math.abs(l.y-bindingLabel.y)<1);
    if(bindingButton()?.text!=='D')throw new Error('Expected authored D binding for the reversible key check');
    await click(bindingButton().x,bindingButton().y); label('Press a key');
    await key('F13');
    if(bindingButton()?.text!=='F13')throw new Error('Function-key rebind failed');
    await click(bindingButton().x,bindingButton().y); label('Press a key');
    await key('d','KeyD');
    if(bindingButton()?.text!=='D')throw new Error('Letter-key rebind did not restore authored binding');
    const workspace=label('Workspace'); await click(workspace.x,workspace.y);
    label('Density'); label('Sidebar side');
    // Two clicks restore the original preference. Compare rendered checkbox
    // pixels; checking only dispatch completion would miss swallowed input.
    const toggle=label('Show agent status indicators');
    const bounds=canvas.getBoundingClientRect();
    canvas.dispatchEvent(new PointerEvent('pointermove',{clientX:bounds.left+toggle.x*bounds.width/canvas.width,clientY:bounds.top+toggle.y*bounds.height/canvas.height}));
    await wait();
    const pixels=()=>Array.from(canvas.getContext('2d').getImageData(toggle.x-32,toggle.y-8,22,18).data);
    const before=pixels(); await click(toggle.x,toggle.y); const changed=pixels();
    await click(toggle.x,toggle.y); const restored=pixels();
    if(JSON.stringify(before)===JSON.stringify(changed)) throw new Error('Settings checkbox did not change');
    if(JSON.stringify(before)!==JSON.stringify(restored)) throw new Error('Settings checkbox did not restore');
    // Check the panel itself: native scroll clamping can legitimately change
    // session-row positions when a long sidebar gains or loses panel height.
    label('No open files.');
    await key('F9');
    if(labels().some(l=>l.text==='No open files.'))throw new Error('F9 did not hide the open-files panel');
    await key('F9');
    label('No open files.');
    await key('F12');
    if(labels().some(l=>l.text==='Sidebar side'))throw new Error('F12 did not return from settings');
    await key('F12');
    if(JSON.parse(managed.WorkspaceState()).windowOpen)throw new Error('F12 did not close the terminal');
    await key('F12');
    const state=JSON.parse(managed.WorkspaceState());
    if(!state.windowOpen || !state.terminalDrawn || state.game.hasGame || state.game.hasWorld || state.game.maps!==null)
      throw new Error('Workspace lost its terminal or Entry invariant');
    if(state.frame<=initial.frame || state.elapsed<=initial.elapsed || state.game.ticks!==0)
      throw new Error('UI clock did not advance independently of simulation');
    return {settings:true,authoredBindings:true,keyReboundAndRestored:true,preferenceChangedAndRestored:true,keyboardLayoutToggle:true,terminalReopened:true,uiClockAdvanced:true,game:state.game};
  } finally {managed.WorkspaceUpdate=original;}
})()
