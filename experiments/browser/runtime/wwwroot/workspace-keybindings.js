// This origin snapshot contains authored SlopWorld bindings only. Managed code
// validates names and key codes; no native XML or host profile enters storage.
const key = 'slopworld.keybindings.v1';
const limit = 131072;

export function installWorkspaceKeyBindings(probe, view = globalThis) {
  let restored = '{}';
  try {
    const text = view.localStorage.getItem(key);
    if (text && text.length <= limit) {
      const saved = JSON.parse(text);
      if (saved?.version === 1 && saved.bindings && !Array.isArray(saved.bindings)
          && typeof saved.bindings === 'object') restored = JSON.stringify(saved.bindings);
    }
  } catch { /* Missing/denied storage keeps native defaults. */ }
  probe.WorkspaceKeyBindings(restored, json => {
    try {
      if (json.length <= limit)
        view.localStorage.setItem(key, JSON.stringify({version: 1, bindings: JSON.parse(json)}));
    } catch { /* Quota/denial must not interrupt native binding controls. */ }
  });
}
