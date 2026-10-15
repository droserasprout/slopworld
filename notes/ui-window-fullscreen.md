# Linux window-manager integration

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

`LinuxGameWindow` owns native titles, application identity, and fullscreen through X11/EWMH; `WindowTitle`
uses its title setter. The Linux launcher sets Waylith's `FORCE_X11=1` before window
creation; the mod follows its saved fullscreen preference while keeping the renderer
and RimWorld display-mode preference windowed. Native fullscreen controls are hidden
in the embedded game options. This path requires X11/EWMH,
including under XWayland; it does not categorically bypass Wayland desktops.
The window watch sets both WM_CLASS strings to `slopworld`, matching the packaged
desktop entry's StartupWMClass. This keeps SlopWorld separate from vanilla RimWorld
in application switchers despite sharing the `RimWorld` executable.
Window checks run during startup and loading transitions, then stop after a quiet
settling period. Next planet arms the watch before leaving the colony; pending landing
and queued long events also restart a retired watch and keep it active through loading.
This includes new colony generation after a failed load returns to the menu.
Unsupported native integration remains disabled for the process lifetime; loading
and explicit scene watches do not restart it. Temporary X11 display unavailability
remains retryable.
The watch verifies native fullscreen state and game surface dimensions as well as
decorations. Geometry recovery resizes the surface before reapplying fullscreen,
without raising or activating the window. Settling waits for confirmed recovery.
If the surface is still incorrectly sized after a recovery request, the next attempt
retries the surface resize after allowing the window manager time to apply fullscreen.

The game's render surface and the outer window must reach monitor geometry together.
Initial fullscreen follows window resize/maximize and remains the final geometry
change. Updating only outer bounds can misalign pixels and input coordinates.
`LinuxWindowRecovery` owns process-lifetime recovery state, watch/retry timing,
and resize-deadline decisions without game dependencies.
[LinuxGameWindow](../mod/Source/SlopWorld/Patches/Chrome/LinuxGameWindow.cs) samples
game state and owns game/X11 calls and their ordering. Game-free tests cover the
policy directly; they do not establish native window-manager behavior. Launch options belong to
[Display settings](../docs/src/customization/settings.md#display).

`SwapchainAcquireTimeout` bounds Vulkan image acquisition to 16 ms on Linux. When
a fullscreen surface stops supplying images, the renderer uses its existing timeout
result to skip that frame without resetting its submission fence. The render queue
can then drain, leaving the main thread free to process X11 events. Keep this wait
bounded independently of focus: the queued render frame can outlive the focus state
that produced it. The patch validates the acquisition call and timeout IL together.
