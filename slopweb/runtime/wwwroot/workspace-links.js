// Native hit testing chooses the URL; dispatch stays in the DOM click stack.
export function attachWorkspaceLinks(managed, view = window) {
  let disposed = false;
  managed.WorkspaceOpenUrl(value => {
    if (disposed) return;
    let url;
    try { url = new URL(value); }
    catch { return; }
    // Terminal output is untrusted. Never execute script or open local files.
    if (!['http:', 'https:', 'mailto:'].includes(url.protocol)) return;
    view.open(url.href, '_blank', 'noopener,noreferrer');
  });
  return {dispose() { disposed = true; managed.WorkspaceOpenUrl(() => {}); }};
}
