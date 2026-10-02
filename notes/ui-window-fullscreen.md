# Linux window-manager integration

`LinuxGameWindow` owns native titles and fullscreen through X11/EWMH; `WindowTitle`
uses its title setter. The launcher supplies default Linux windowed/OpenGL arguments,
while the mod follows the saved fullscreen preference. This path requires X11/EWMH,
including under XWayland; it does not categorically bypass Wayland desktops.

Unity's render surface and the outer window must reach monitor geometry together.
Initial fullscreen follows window resize/maximize and remains the final geometry
change. Updating only outer bounds can misalign pixels and input coordinates.
The [source](../mod/Source/SlopWorld/Patches/Chrome/LinuxGameWindow.cs) owns platform
calls and retry sequencing. Launch options belong to
[Linux window options](../docs/src/build.md#linux-window-options).
