# Linux window integration

`Bootstrap/IdleInhibition` gates Linux idle inhibition on the active profile and its
live preference. Its lifetime belongs to the game client, independently of the launcher,
daemon, and colony. `IdleInhibitionWorker` owns acquisition and cleanup off the UI thread;
shutdown must remain bounded even when native calls stall or acquisition finishes late.

The desktop idle lease uses the portal or ScreenSaver D-Bus service.

Waylith runs on Wayland and owns borderless fullscreen, decorations,
and the title. `LinuxGameWindow` follows the saved SlopWorld preference through
the game display mode. Waylith sizes the surface from the compositor's resize
events. Each main xdg_toplevel receives the app ID `slopworld`, matching the installed
desktop entry and icon. Wayland uses game wheel events.

Game-free tests verify policy, not native window-manager behavior. User controls
belong to [Display settings](../docs/src/customization/settings.md#display).

`SwapchainAcquireTimeout` bounds Vulkan image acquisition to 16 ms. When
a fullscreen surface stops supplying images, the renderer uses its existing timeout
result to skip that frame without resetting its submission fence. The render queue
can then drain, leaving the main thread free to process Wayland events. Keep this wait
bounded independently of focus: the queued render frame can outlive the focus state
that produced it. The patch preserves shorter resize waits and returns the first timeout through
native cleanup so retries cannot extend the stall.
