import {settingsSnapshot} from './browser-storage.js';
// This origin snapshot contains authored SlopWorld bindings only. Managed code
// validates names and key codes; no native XML or host profile enters storage.
export function installWorkspaceKeyBindings(probe, view = globalThis) {
  probe.WorkspaceKeyBindings(...settingsSnapshot(view, 'slopworld.keybindings.v1', 'bindings'));
}
