# Linux window integration

`LinuxGameWindow` owns X11/EWMH titles, application identity, and fullscreen, including
under XWayland. The launcher supplies Linux windowed/OpenGL defaults; the mod follows
the saved fullscreen preference. `--no-window-fix` removes launcher defaults but does
not disable the mod hook. Both WM_CLASS strings are `slopworld`, matching the desktop
entry despite sharing the RimWorld executable.

`LinuxWindowRecovery` owns process-lifetime policy and retry timing without game
dependencies. `LinuxGameWindow` samples scene state and performs ordered Unity/X11
calls. Startup and loading arm the watch; quiet settlement retires it. Departure,
pending landing, and queued loads must rearm it, including after failed loads.
Unsupported integration stays disabled for the process; temporary display loss retries.

The Unity surface and outer window must reach monitor geometry together or pixels
and input diverge. Resize the surface before applying fullscreen, keep fullscreen as
the final geometry change, and do not raise/activate the window. Verify actual surface
and native fullscreen state before settling; persistent mismatch retries after the
window manager has time to respond.

Game-free tests verify policy, not native window-manager behavior. User controls
belong to [Display settings](../docs/src/customization/settings.md#display).
