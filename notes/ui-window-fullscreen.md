# Linux window-manager integration

`Bootstrap/IdleInhibition` owns the Linux desktop idle-inhibition lease in the game
client, independently of the launcher and daemon. It starts only in the active
SlopWorld profile and covers menus and play. Display > Window > Inhibit screensaver
is a live profile preference, enabled by default. Disabling it signals cleanup off
the UI thread; enabling it starts a fresh lease. Disabled startup does not load
the native libraries. `IdleInhibitionWorker` owns acquisition,
lease lifetime and release off the game thread. Application quit and process exit
request bounded cleanup; a late acquisition after quit is immediately released.
`LinuxIdleInhibitor` tries the XDG portal, then freedesktop ScreenSaver, on separate
private connections through system libdbus. Portal subscription precedes the request
and acquisition requires a successful Response. Failed attempts disconnect before
fallback. Both protocols release on connection loss, including abrupt game exit.
`LinuxIdleLease` combines that desktop lease with `X11IdleInhibitor`, which owns a
separate worker-only display connection. The worker resets the X11 idle timer
immediately and every 30 seconds, even with a desktop lease. If both D-Bus protocols
fail, it also suspends the X screensaver through MIT-SCREEN-SAVER 1.1 when available
and sends legacy GNOME/freedesktop activity notifications. Missing libXss or an old
extension leaves periodic X11 resets active. Closing the private display releases
suspension, including on process exit. X11 and desktop acquisition fail independently;
the client warns only if neither is available and continues startup. Native
connection/authentication may stall its background worker but never game frames.

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
