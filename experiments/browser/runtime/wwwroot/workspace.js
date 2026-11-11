// Continuous presentation of the actual mod WindowStack. No DOM agent controls.
import { attachGuiInput } from './gui-input.js';
import { attachInput } from './input.js';
import { workspaceViewport, workspaceParameters, workspaceOption } from './workspace-viewport.js';
import { loadBackground } from './loading-background.js';
import { attachWorkspaceCursor } from './workspace-cursor.js';
import { WorkspaceScheduler } from './workspace-scheduler.js';

export function startWorkspace(managed, canvas, initialTime = 0) {
  canvas.tabIndex = 0;
  canvas.style.cssText = `display:block;width:100%;max-width:${canvas.width}px;outline:none`;
  document.body.prepend(canvas);
  const started = performance.now();
  const backgroundAbort = new AbortController();
  const retainedBackground = window.__loading?.takeBackground?.();
  let background = retainedBackground?.animation ?? null;
  let backgroundStarted = retainedBackground?.started ?? performance.now()/1000;
  if (!background) loadBackground(backgroundAbort.signal).then(animation => {
    if (backgroundAbort.signal.aborted) { animation?.dispose(); return; }
    background=animation;backgroundStarted=performance.now()/1000;
  }).catch(error=>{if(error.name!=='AbortError') console.warn('Workspace background unavailable:',String(error));});
  const parameters = workspaceParameters();
  managed.WorkspacePacketCache(workspaceOption(parameters,'packetCache'));
  const idleYield=workspaceOption(parameters,'idleYield');
  let reuseCanvas=workspaceOption(parameters,'reuseCanvas')||idleYield;
  let scale = workspaceViewport(parameters).uiScale;
  const resize = () => {
    if (!parameters.has('responsive')) return;
    const viewport = workspaceViewport(parameters);
    if (canvas.width === viewport.width && canvas.height === viewport.height && scale === viewport.uiScale) return;
    managed.ConfigureWorkspaceViewport(viewport.width, viewport.height, viewport.uiScale);
    canvas.width = viewport.width; canvas.height = viewport.height; scale = viewport.uiScale;
    document.documentElement.style.setProperty('--workspace-aspect', viewport.width / viewport.height);
    document.body.style.setProperty('--workspace-aspect', viewport.width / viewport.height);
  };
  const device = attachInput(canvas, managed, true);
  const cursor = attachWorkspaceCursor(canvas, managed);
  // Opt-in bounded measurements; ordinary presentation does not retain per-frame data.
  let profile = parameters.has('profile') ? [] : null;
  managed.WorkspaceProfile(profile !== null);
  if (profile) window.__workspaceProfile = profile;
  const recordFrame = sample => { if (profile.length === 4096) profile.shift(); profile.push(sample); };
  const gui = attachGuiInput(canvas, {
    // Clipboard activation flushes synchronously on pointer/key events too.
    // Native capture must see the matching physical button state in that frame.
    beforeFrame: () => device.frame(),
    get allowFrameReuse() { return reuseCanvas && !managed.WorkspaceBackgroundVisible(); },
    drawBackground(context) {
      if (!managed.WorkspaceBackgroundVisible() || !background) return;
      background.advance(performance.now()/1000-backgroundStarted);
      context.save();context.setTransform(scale,0,0,scale,0,0);
      background.draw(context,canvas.width/scale,canvas.height/scale);
      context.restore();
    },
    get profileFrame() { return profile ? recordFrame : null; },
    ClipboardTake: () => managed.ClipboardTake(),
    ClipboardComplete: (...args) => managed.ClipboardComplete(...args),
    ClipboardCompleteImage: (...args) => managed.ClipboardCompleteImage(...args),
    GuiInputFrame: batch => managed.WorkspaceUpdate(batch, initialTime + (performance.now() - started) / 1000),
  });
  let stopped = false;
  const scheduler=new WorkspaceScheduler(()=>update());
  const activity=new AbortController();
  for (const target of [canvas, gui.textInput])
  for(const event of ['input','compositionstart','compositionupdate','keydown','keyup','pointerdown','pointerup','pointermove','wheel','paste','copy','compositionend','blur'])
    target.addEventListener(event,()=>scheduler.wake(true),{signal:activity.signal});
  for(const event of ['resize','focus','blur'])window.addEventListener(event,()=>scheduler.wake(true),{signal:activity.signal});
  document.addEventListener('visibilitychange',()=>scheduler.wake(true),{signal:activity.signal});
  managed.WorkspaceWake(()=>scheduler.wake());
  const state = { status: 'starting', frames: 0, source: 'SlopWorld WindowStack', game: JSON.parse(managed.GameSnapshot()) };
  state.setCanvasReuse=enabled=>{const previous=reuseCanvas;reuseCanvas=enabled;return previous;};
  state.setIdleYield=enabled=>scheduler.setEnabled(enabled);
  state.setProfiling = enabled => {
    profile = enabled ? [] : null;
    window.__workspaceProfile = profile;
    managed.WorkspaceProfile(enabled);
  };
  const stop = () => {
    if (stopped) return;
    stopped = true; scheduler.stop();activity.abort();managed.WorkspaceClearWake();
    backgroundAbort.abort();background?.dispose();background=null;
    device.dispose(); gui.dispose();
    cursor.dispose();
  };
  const update = () => {
    if (stopped) return;
    try {
      state.frameStarted=performance.now();
      resize();
      const result = gui.frame();
      cursor.frame();
      state.commands = result.commands.length;
      if (state.frames % 60 === 0) {
        state.game = JSON.parse(managed.GameSnapshot());
        state.workspace = JSON.parse(managed.WorkspaceState());
      }
      state.frames++; state.status = 'running';
      return result.presentationUnchanged;
    } catch (error) {
      state.status = 'fail'; state.error = String(error); state.stack = error.stack;
      document.querySelector('#result').textContent = JSON.stringify(state, null, 2);
      document.body.classList.remove('ready');
      stop();
    }
  };
  window.addEventListener('pagehide', stop, { once: true });
  scheduler.setEnabled(idleYield);
  window.__workspace = state;
  return state;
}
