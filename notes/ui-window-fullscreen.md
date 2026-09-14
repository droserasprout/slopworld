# Linux fullscreen trap

The launcher starts Unity windowed with the X11/OpenGL path; `MaximizeWindow.cs` then uses
EWMH fullscreen. Native Unity fullscreen can freeze on Alt+Tab or stretch the client surface.
Wayland bypasses this hook; `--no-window-fix` is the opt-out.

Window resize is asynchronous. Establish monitor-sized windowed rendering/maximize first,
then apply fullscreen after the resize lands (or times out), so fullscreen is the final
geometry change. Keep maximize underneath to avoid restoring an arbitrary startup size.

Mutter may restore decorations when fullscreen is removed, so the hook also disables them
through Motif hints. Preserve both render-surface and window-manager transitions; changing
only the outer window can desynchronize pixels and input coordinates.
