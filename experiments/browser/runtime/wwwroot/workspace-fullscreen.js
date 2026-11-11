// Keep native window intent inside the trusted DOM input stack. Never optimistically
// set the native checkbox: Escape, browser UI, and rejected requests report actual state.
export function attachWorkspaceFullscreen(managed, view = window) {
  const doc = view.document;
  const abort = new AbortController();
  let disposed = false;
  const sync = () => {
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
  sync();
  return {dispose() { disposed = true; abort.abort(); managed.WorkspaceWindow(() => {}); }};
}
