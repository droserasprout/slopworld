# Linux window integration

`Bootstrap/IdleInhibition` owns the Linux desktop idle-inhibition lease in the game
client, independently of the launcher and daemon. It starts only in the active
SlopWorld profile and covers menus and play. Display > Window > Inhibit screensaver
is a live profile preference, enabled by default. Disabling it signals cleanup off
the UI thread; enabling it starts a fresh lease. Disabled startup does not load
the native libraries. `IdleInhibitionWorker` owns acquisition,
lease lifetime and release off the game thread. Application quit and process exit
request bounded cleanup; a late acquisition after quit is immediately released.
`LinuxIdleInhibitor` tries the XDG portal, then freedesktop ScreenSaver, on separate
private managed connections through Tmds.DBus.Protocol. Portal subscription precedes
the request and acquisition requires a successful Response. Failed attempts disconnect
before fallback. Both protocols release on connection loss, including abrupt game exit.
Each refresh
checks that the granting service still owns its bus name. Service loss triggers
reacquisition, with X11 suspension and legacy activity enabled while it is unavailable.
Release addresses the original unique owner so a restarted service cannot receive an
old cookie.
`LinuxIdleLease` combines that desktop lease with `X11IdleInhibitor`, which owns a
separate worker-only display connection. The worker resets the X11 idle timer
immediately and every 30 seconds, even with a desktop lease. If both D-Bus protocols
fail, it also suspends the X screensaver through MIT-SCREEN-SAVER 1.1 when available
and sends legacy GNOME/freedesktop activity notifications. Missing libXss or an old
extension leaves periodic X11 resets active. Closing the private display releases
suspension, including on process exit. X11 and desktop acquisition fail independently;
the client warns only if neither is available and continues startup. Bus connection,
authentication, calls and responses have bounded waits; disposal cancels unfinished
transport work. X11 calls remain on the background worker.

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
