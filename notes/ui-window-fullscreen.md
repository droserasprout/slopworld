# Linux fullscreen trap

The launcher starts Unity in windowed mode with the X11/OpenGL path.
`LinuxGameWindow` owns X11 window discovery, titles, and EWMH fullscreen. Native Unity fullscreen can freeze on Alt+Tab or stretch the client surface.
Wayland bypasses this hook.
Use `--no-window-fix` to disable it.

Window resize is asynchronous. First establish monitor-sized windowed rendering and maximization.
Apply fullscreen after the resize completes or times out.
Fullscreen must be the final geometry change. Keep maximize underneath to avoid restoring an arbitrary startup size.

Mutter may restore decorations when it exits fullscreen mode. The hook also disables them
through Motif hints. Xlib format-32 properties consume native C longs, so pinned hint
storage must use native-word-sized elements even though each property value is 32 bits. Preserve both render-surface and window-manager transitions.
Changing only the outer window can misalign pixels and input coordinates.
