// Keep native window intent inside the trusted DOM input stack. Never optimistically
// set the native checkbox: Escape, browser UI, and rejected requests report actual state.
export function attachWorkspaceFullscreen(managed, view = window) {
  const doc = view.document;
  const abort = new AbortController();
  let disposed = false;
  const keyboard = view.navigator?.keyboard;
  let lockedFullscreen = false;
  const sync = () => {
    const fullscreen = !disposed && doc.fullscreenElement === doc.documentElement;
    if (fullscreen !== lockedFullscreen) {
      lockedFullscreen = fullscreen;
      if (fullscreen && keyboard?.lock) {
        // Optional browser permission: rejection leaves normal fullscreen usable.
        try {
          Promise.resolve(keyboard.lock(['Escape', 'F11'])).then(() => {
            if (disposed || !lockedFullscreen) keyboard.unlock?.();
          }, () => {});
        }
        catch { /* Unsupported or denied keyboard capture. */ }
      } else keyboard?.unlock?.();
    }
    if (!disposed) managed.WorkspaceFullscreenState(doc.fullscreenElement === doc.documentElement);
  };
  const request = fullscreen => {
    if (disposed) return;
    try {
      const operation = fullscreen
        ? (doc.fullscreenElement === doc.documentElement ? null : doc.documentElement.requestFullscreen())
        : (doc.fullscreenElement ? doc.exitFullscreen() : null);
      Promise.resolve(operation).then(sync, error => {
        sync();
        view.console?.warn('Fullscreen request rejected:', String(error));
      });
    } catch (error) {
      sync();
      view.console?.warn('Fullscreen unavailable:', String(error));
    }
  };
  managed.WorkspaceWindow(request);
  doc.addEventListener('fullscreenchange', sync, {signal: abort.signal});
  // Escape remains native terminal/UI input. F11 owns fullscreen toggling.
  doc.addEventListener('keydown', event => {
    if (event.key !== 'F11') return;
    event.preventDefault();
    event.stopPropagation();
    if (!event.repeat) request(doc.fullscreenElement !== doc.documentElement);
  }, {capture: true, signal: abort.signal});
  sync();
  return {dispose() { disposed = true; keyboard?.unlock?.(); abort.abort(); managed.WorkspaceWindow(() => {}); }};
}
