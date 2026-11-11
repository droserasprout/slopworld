// Evaluate in an already-loaded workspace. Exercise actual mod settings through
// DOM events, then restore the local preference and return to the terminal.
(async () => {
  const managed = window.__managedProbe, canvas = document.querySelector('canvas');
  if (window.__workspace?.status !== 'running') throw new Error('Workspace is not running');
  const initial = JSON.parse(managed.WorkspaceState());
  managed.WorkspaceSavePreferences();
  const preferenceKey='slopworld.presentation.v1',bindingKey='slopworld.keybindings.v1';
  const savedPreferences=localStorage.getItem(preferenceKey),savedBindings=localStorage.getItem(bindingKey);
  let bindingTouched=false;
  const previousLabels = managed.WorkspaceObserveLabels(true);
  const wait = () => new Promise(resolve => setTimeout(resolve, 350));
  const labels = () => JSON.parse(managed.WorkspaceLabels()).map(l => ({
    text:l.text, x:l.x+Math.min(l.width/2,5*initial.uiScale), y:l.y+l.height/2
  }));
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
    if (!labels().some(l=>l.text==='Appearance')) {
      const door=JSON.parse(managed.WorkspaceState()).doors.config;
      if(!door.visible)throw new Error('Settings door is not visible');
      const [x,y,w,h]=door.rect;await click((x+w/2)*initial.uiScale,(y+h/2)*initial.uiScale);
    }
    const keyboard=label('Keyboard'); await click(keyboard.x,keyboard.y);
    label('Open/close agent terminal'); label('F12');
    const bindingLabel=label('Duplicate selected agent');
    const bindingButton=()=>labels().find(l=>l.x>bindingLabel.x+300 && Math.abs(l.y-bindingLabel.y)<1);
    if(bindingButton()?.text!=='D')throw new Error('Expected authored D binding for the reversible key check');
    await click(bindingButton().x,bindingButton().y); label('Press a key');
    bindingTouched=true;
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
    const fileLabel=labels().find(l=>l.text==='No open files.')
      ?? labels().find(l=>l.x<150 && l.y>30 && l.y<230 && l.text.length>3);
    if(!fileLabel)throw new Error('Missing visible open-files panel content');
    await key('F9');
    if(labels().some(l=>l.text===fileLabel.text && l.x<150 && l.y>30 && l.y<230))throw new Error('F9 did not hide the open-files panel');
    await key('F9');
    label(fileLabel.text);
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
  } finally {
    managed.WorkspaceObserveLabels(previousLabels);
    if(savedPreferences!==null) {
      localStorage.setItem(preferenceKey,savedPreferences);
      managed.WorkspacePreferences(JSON.stringify(JSON.parse(savedPreferences).preferences),json=>{
        localStorage.setItem(preferenceKey,JSON.stringify({version:1,preferences:JSON.parse(json)}));
      });
    }
    if(bindingTouched) {
      localStorage.setItem(bindingKey,savedBindings??JSON.stringify({version:1,bindings:{SlopDuplicateSession:{a:'D'}}}));
      (await import('/workspace-keybindings.js')).installWorkspaceKeyBindings(managed);
      if(savedBindings===null)localStorage.removeItem(bindingKey);
    }
  }
})()
