// Continuous presentation of the actual mod WindowStack. No DOM agent controls.
import { attachGuiInput } from './gui-input.js';
import { attachInput } from './input.js';

export function startWorkspace(managed, canvas, initialTime = 0) {
  canvas.tabIndex = 0;
  canvas.style.cssText = 'display:block;width:100%;max-width:1280px;outline:none';
  document.body.prepend(canvas);
  const started = performance.now();
  const device = attachInput(canvas, managed, true);
  const gui = attachGuiInput(canvas, {
    ClipboardTake: () => managed.ClipboardTake(),
    ClipboardComplete: (...args) => managed.ClipboardComplete(...args),
    GuiInputFrame: batch => managed.WorkspaceUpdate(batch, initialTime + (performance.now() - started) / 1000),
  });
  let stopped = false, frame = 0;
  const state = { status: 'starting', frames: 0, source: 'SlopWorld WindowStack', game: JSON.parse(managed.GameSnapshot()) };
  const stop = () => {
    if (stopped) return;
    stopped = true; cancelAnimationFrame(frame);
    device.dispose(); gui.dispose();
  };
  const update = () => {
    if (stopped) return;
    try {
      device.frame();
      const result = gui.frame();
      state.commands = result.commands.length;
      if (state.frames % 60 === 0) {
        state.game = JSON.parse(managed.GameSnapshot());
        state.workspace = JSON.parse(managed.WorkspaceState());
      }
      state.frames++; state.status = 'running';
      frame = requestAnimationFrame(update);
    } catch (error) {
      state.status = 'fail'; state.error = String(error); state.stack = error.stack;
      document.querySelector('#result').textContent = JSON.stringify(state, null, 2);
      document.body.classList.remove('ready');
      stopped = true;
    }
  };
  window.addEventListener('pagehide', stop, { once: true });
  frame = requestAnimationFrame(update);
  window.__workspace = state;
  return state;
}
