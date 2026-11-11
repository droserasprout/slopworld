// Storage belongs to this browser origin. Only the managed presentation allowlist
// crosses this boundary; hostHome and the native TOML profile are never stored.
const key = 'slopworld.presentation.v1';
const limit = 131072;

export function installWorkspacePreferences(probe, view = globalThis) {
  let restored = '{}';
  try {
    const text = view.localStorage.getItem(key);
    if (text && text.length <= limit) {
      const saved = JSON.parse(text);
      if (saved?.version === 1 && saved.preferences && !Array.isArray(saved.preferences)
          && typeof saved.preferences === 'object')
        restored = JSON.stringify(saved.preferences);
    }
  } catch { /* Disabled storage or a damaged snapshot keeps the host defaults. */ }
  probe.WorkspacePreferences(restored, json => {
    try {
      if (json.length <= limit)
        view.localStorage.setItem(key, JSON.stringify({version: 1, preferences: JSON.parse(json)}));
    } catch { /* Storage denial/quota must not interrupt native settings controls. */ }
  });
  // Native slider changes are debounced in frames; preserve the latest values
  // when a tab reloads or is backgrounded before that native deadline.
  view.addEventListener('pagehide', () => probe.WorkspaceSavePreferences());
  view.document.addEventListener('visibilitychange', () => {
    if (view.document.visibilityState === 'hidden') probe.WorkspaceSavePreferences();
  });
}
