// Close the native fullscreen workspace through DOM F12, then reopen it using
// the selected session's actual Terminal gizmo. Never type into its terminal.
(async () => {
  if (window.__workspace?.status !== 'running') throw new Error('Workspace not running');
  const managed = window.__managedProbe;
  const {renderGui} = await import('./gui.js');
  const canvas = document.querySelector('canvas:not(#loading-screen)');
  const state = () => JSON.parse(managed.WorkspaceState());
  const initial = state();
  if (!initial.windowOpen || !initial.session || initial.doors.view)
    throw new Error('Run from an open terminal workspace');
  const wait = () => new Promise(resolve => setTimeout(resolve, 250));
  const key = async () => {
    for (const type of ['keydown', 'keyup']) {
      canvas.dispatchEvent(new KeyboardEvent(type, {key:'F12', code:'F12', bubbles:true}));
      await wait();
    }
  };
  const click = async (rect, snapshot) => {
    const [x,y,w,h] = rect, bounds = canvas.getBoundingClientRect();
    for (const type of ['pointerdown', 'pointerup']) {
      canvas.dispatchEvent(new PointerEvent(type, {
        button:0, bubbles:true,
        clientX:bounds.left+(x+w/2)*bounds.width/snapshot.uiWidth,
        clientY:bounds.top+(y+h/2)*bounds.height/snapshot.uiHeight,
      }));
      await wait();
    }
  };
  try {
    await key();
    const closed = state();
    if (closed.windowOpen || !closed.underlying.visible || !closed.underlying.chromeVisible)
      throw new Error('F12 did not expose the native underlying workspace');
    if (closed.underlying.session !== initial.session || closed.underlying.actions[0] !== 'Terminal')
      throw new Error('Selected session native actions did not survive F12');
    const rect = closed.underlying.firstActionRect;
    if (!rect || rect[0] < 0 || rect[1] < 0 || rect[0]+rect[2] > closed.uiWidth || rect[1]+rect[3] > closed.uiHeight)
      throw new Error('Native action geometry is outside the viewport');
    if (closed.game.hasGame || closed.game.hasWorld || closed.game.ticks)
      throw new Error('Underlying workspace crossed the Entry boundary');
    const sample=()=>canvas.getContext('2d').getImageData(Math.floor(canvas.width*.6),Math.floor(canvas.height*.4),100,100).data;
    const before=sample();
    const deadline=performance.now()+5000;
    while(!sample().some((value,index)=>value!==before[index])) {
      if(performance.now()>deadline)throw new Error('Closed-workspace background did not animate');
      await wait();
    }
    // The real command handles the click and calls WorkspaceWindow.Open itself.
    await click(rect, closed);
    const reopened = state();
    if (!reopened.windowOpen || reopened.session !== initial.session || reopened.underlying.visible)
      throw new Error('Native Terminal action did not restore the selected workspace');
    const errors = reopened.errors.filter(error => !initial.errors.includes(error));
    const drawingErrors=errors.filter(error=>error!=='Cannot play Click (subSound Click_0_: No resolved grains.');
    if (drawingErrors.length) throw new Error('Native drawing errors: '+JSON.stringify(drawingErrors));
    return {closed:closed.underlying, backgroundAnimated:true, restored:reopened.session, game:reopened.game,diagnostics:errors};
  } finally {
    if (!state().windowOpen || state().session !== initial.session)
      renderGui(canvas,JSON.parse(managed.WorkspaceSelect(initial.session)));
  }
})()
