import {settingsSnapshot} from './browser-storage.js';
// Storage belongs to this browser origin. The managed allowlist selects the
// presentation and jukebox preferences that survive reload.
export function installWorkspacePreferences(probe, view = globalThis) {
  probe.WorkspacePreferences(...settingsSnapshot(view, 'slopworld.presentation.v1', 'preferences'));
  // Native slider changes are debounced in frames; preserve the latest values
  // when a tab reloads or is backgrounded before that native deadline.
  view.addEventListener('pagehide', () => probe.WorkspaceSavePreferences());
  view.document.addEventListener('visibilitychange', () => {
    if (view.document.visibilityState === 'hidden') probe.WorkspaceSavePreferences();
  });
}
