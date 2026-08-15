# Linux game window fullscreen

The `slopworld` runner passes `-popupwindow -screen-fullscreen 0 -force-opengl` by
default. Unity's Linux fullscreen path is avoided because it can freeze on Alt+Tab
and produce a stretched client surface. `--no-window-fix` omits those arguments for
an alternate windowing setup. After the popup is mapped, `Patches/MaximizeWindow.cs`
sends the X11 EWMH `_NET_WM_STATE_FULLSCREEN` request to the game's Xwayland
client; that lets the window manager resize the client and its input coordinates
together while still presenting as fullscreen to GNOME. The first request waits for
RimWorld's long loading event and a short Unity resize-settling interval; requesting
fullscreen during that transition leaves the upper client region stale until toggled.
The same hook writes `_MOTIF_WM_HINTS` with decorations disabled, because Mutter can
restore Unity's titlebar when the EWMH fullscreen state is removed. The off state removes
fullscreen and adds horizontal/vertical EWMH maximize, preserving complete workarea
geometry without bringing the titlebar back. Maximize is kept underneath fullscreen so
each toggle has one visible resize; it is established before the first fullscreen request
so exiting cannot restore Unity's arbitrary window size. The client is raised/activated
after the request.

Keep the X11/OpenGL path. `-force-wayland` bypasses this X11 window-manager hook.
