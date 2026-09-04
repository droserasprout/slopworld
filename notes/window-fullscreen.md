# Linux game window fullscreen

The `slopworld` runner passes `-popupwindow -screen-fullscreen 0 -force-opengl` by
default. Unity's Linux fullscreen path is avoided because it can freeze on Alt+Tab
and produce a stretched client surface. `--no-window-fix` omits those arguments for
an alternate windowing setup. After the popup is mapped, `Patches/MaximizeWindow.cs`
sends the X11 EWMH `_NET_WM_STATE_FULLSCREEN` request to the game's Xwayland
client; that lets the window manager resize the client and its input coordinates
together while still presenting as fullscreen to GNOME. The first request fires
during the loading screen, after a short settle from the mod's first frame lets the
game window get mapped. The initial application induces the required resize with
`Screen.SetResolution(monitor, FullScreenMode.Windowed)`, which rebuilds Unity's
render surface at the monitor size so the EWMH request lands on a correctly sized
surface. Windowed mode keeps that resync off Unity's frozen fullscreen path.
The first application runs in two phases: phase one rebuilds the surface and sets
maximize, phase two adds fullscreen only once the windowed resize has landed (Screen
reaching the monitor size, or a short timeout). Because Unity applies SetResolution a
frame late, fullscreen is applied last so it remains the final geometry change.
Follow polls fast between the two phases.
The same hook writes `_MOTIF_WM_HINTS` with decorations disabled, because Mutter can
restore Unity's titlebar when the EWMH fullscreen state is removed. The off state removes
fullscreen and adds horizontal/vertical EWMH maximize, preserving complete workarea
geometry without bringing the titlebar back. Maximize is kept underneath fullscreen so
each toggle has one visible resize; it is established before the first fullscreen request
so exiting cannot restore Unity's arbitrary window size. The client is raised/activated
after the request.
The PID-based X11 lookup also writes `WM_NAME` and `_NET_WM_NAME`: native daemon connections use
`SlopWorld`, while a `slopcar` capability changes it to `SlopWorld [s]`.

Keep the X11/OpenGL path. `-force-wayland` bypasses this X11 window-manager hook.
