# Linux window-manager integration

`LinuxGameWindow` owns native titles and fullscreen through X11/EWMH; `WindowTitle`
uses its title setter. The launcher supplies default Linux windowed/OpenGL arguments,
while the mod follows the saved fullscreen preference. This path requires X11/EWMH,
including under XWayland; it does not categorically bypass Wayland desktops.
Window checks run only during startup and the Next planet menu/loading transition,
then stop after a quiet settling period. The transition arms the watch before leaving
the colony; queued long events keep it active after the pending landing is consumed.
The watch verifies native fullscreen state and Unity surface dimensions as well as
decorations. Geometry recovery resizes the surface before reapplying fullscreen,
without raising or activating the window. Settling waits for confirmed recovery.
If the surface is still incorrectly sized after a recovery request, the next attempt
retries the surface resize after allowing the window manager time to apply fullscreen.

Unity's render surface and the outer window must reach monitor geometry together.
Initial fullscreen follows window resize/maximize and remains the final geometry
change. Updating only outer bounds can misalign pixels and input coordinates.
The [source](../mod/Source/SlopWorld/Patches/Chrome/LinuxGameWindow.cs) owns platform
calls and retry sequencing. Launch options belong to
[Display settings](../docs/src/reference/settings.md#display).
