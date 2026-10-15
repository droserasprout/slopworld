# Linux window integration

`Bootstrap/IdleInhibition` gates Linux idle inhibition on the active profile and its
live preference. Its lifetime belongs to the game client, independently of the launcher,
daemon, and colony. `IdleInhibitionWorker` owns acquisition and cleanup off the UI thread;
shutdown must remain bounded even when native calls stall or acquisition finishes late.

`LinuxIdleLease` combines managed desktop inhibition with an independent X11 idle timer
and repairs lost desktop leases. `LinuxIdleInhibitor` owns portal/ScreenSaver protocol
policy; Tmds owns D-Bus transport. `X11IdleInhibitor` owns a private worker-only display.
Keep both connections private so process exit releases their resources. Protocol ordering
and fallback details stay beside those owners. [C# tests](test-csharp.md) describes the
isolated bus tests; the display preference belongs to [Display settings](../docs/src/customization/settings.md#display).

`LinuxGameWindow` owns X11/EWMH titles, application identity, and fullscreen, including
under XWayland. The launcher sets `FORCE_X11=1` before window creation. The mod follows
its saved fullscreen preference while keeping the renderer and RimWorld display mode
windowed. Native fullscreen controls are hidden in embedded game options. Both WM_CLASS strings are `slopworld`, matching the desktop
entry despite sharing the RimWorld executable.

`LinuxWindowRecovery` owns process-lifetime policy and retry timing without game
dependencies. `LinuxGameWindow` samples scene state and performs ordered game/X11
calls. Startup and loading arm the watch; quiet settlement retires it. Departure,
pending landing, and queued loads must rearm it, including after failed loads.
Unsupported integration stays disabled for the process; temporary display loss retries.

The game surface and outer window must reach monitor geometry together or pixels
and input diverge. Resize the surface before applying fullscreen, keep fullscreen as
the final geometry change, and do not raise/activate the window. Verify actual surface
and native fullscreen state before settling; persistent mismatch retries after the
window manager has time to respond.

Game-free tests verify policy, not native window-manager behavior. User controls
belong to [Display settings](../docs/src/customization/settings.md#display).

`SwapchainAcquireTimeout` bounds Vulkan image acquisition to 16 ms on Linux. When
a fullscreen surface stops supplying images, the renderer uses its existing timeout
result to skip that frame without resetting its submission fence. The render queue
can then drain, leaving the main thread free to process X11 events. Keep this wait
bounded independently of focus: the queued render frame can outlive the focus state
that produced it. The patch validates the acquisition call and timeout IL together.
