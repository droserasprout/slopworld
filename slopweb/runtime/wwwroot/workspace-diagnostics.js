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

// Inspect the already-presented workspace only when startup profiling is enabled.
export function inspectWorkspaceCanvas(canvas) {
  const pixels = canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height).data;
  let visible = 0;
  for (let i = 3; i < pixels.length; i += 4) if (pixels[i]) visible++;
  if (!visible) throw new Error('Installed GUI produced no visible pixels');
  return {visiblePixels: visible, source: 'SlopWorld.WorkspaceWindow → IGuiBackend → Canvas2D'};
}
