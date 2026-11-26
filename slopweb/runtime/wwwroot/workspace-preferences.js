const key = 'slopworld.presentation.v1';

export function installWorkspacePreferences(probe, view = globalThis) {
  // Each allowlisted field has its own atomic write, including during synchronous
  // page teardown. An overlapping tab can only replace a field it edited.
  function read(name) {
    try {
      const json = view.localStorage.getItem(key + '.' + name);
      return json !== null && json.length <= 131072 ? json : '';
    } catch { return ''; }
  }
  function persist(name, json) {
    try {
      if (json.length > 131072) return false;
      view.localStorage.setItem(key + '.' + name, json);
      return true;
    } catch { return false; }
  }
  probe.WorkspacePreferences(read, persist);
  // Native slider changes are debounced in frames; preserve the latest values
  // when a tab reloads or is backgrounded before that native deadline.
  view.addEventListener('pagehide', () => probe.WorkspaceSavePreferences());
  view.document.addEventListener('visibilitychange', () => {
    if (view.document.visibilityState === 'hidden') probe.WorkspaceSavePreferences();
  });
}
