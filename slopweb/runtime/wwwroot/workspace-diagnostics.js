// Optional observations must never enter the render loop's fatal error path.
export function collectWorkspaceDiagnostics(managed, state, enabled) {
  if (!enabled || state.frames % 60 !== 0) return;
  try {
    const snapshot = JSON.parse(managed.WorkspaceState());
    state.workspace = snapshot;
    state.game = snapshot.game;
    delete state.diagnosticError;
  } catch (error) {
    state.diagnosticError = String(error);
  }
}
