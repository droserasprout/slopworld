# Profile settings

`Settings/ModSettings.cs` owns typed profile data, normalization, and dirty timing;
`ModSettingsStore` owns file replacement with caller-supplied paths/errors.
`ProfileSettings` supplies RimWorld paths and runtime adapters. `Settings.cs` projects
the active profile; only preferences consumed there need shim properties.
[Daemon configuration](daemon-config-stores.md) has separate ownership.

Persisted fields require explicit typed entries in `Fields`. Runtime dirty state stays
off disk, and failed timed writes remain pending. Formatting preferences must not
change machine timestamp formats; caches include the selected formats.
`TimeZonePreference` converts UTC instants to profile wall time. Auto uses the game
desktop zone, independently of the daemon. Planner calendar dates and the colony
clock use the same zone; date-only values and elapsed-time clocks do not convert.
Unavailable zone IDs remain saved and fall back to the desktop zone.
[Settings drafts](ui-settings.md) owns apply/save timing.

`FramePolicy` owns foreground and unfocused render limits. `BackgroundFrames` supplies
monitor refresh and applies the policy. VSync can override the FPS selector without
replacing its choice; focus changes adjust the render limit without changing
presentation mode. Eco does not add another foreground pacing policy.

`RenderPacing` gates native frame capture after input and game updates; it replaces
the engine's main-thread FPS sleeps and render-backlog waits. `RenderSchedule`
keeps presentation deadlines separate from bounded service wakeups. Window events
wake the loop immediately, and a full render queue skips capture instead of blocking
updates. Native event waiting must stay after input-frame reset, because the wait
itself dispatches callbacks. Skipped capture still runs the native frame finally;
surface rebuild and minimized-window resource cleanup remain engine-owned.

Temperature-triggered camera/UI sounds belong to `Patches/Options/TemperatureSounds.cs`;
map sounds retain their definitions. Disabling destructive effects through Eco or
Grandma cancels pending effects rather than postponing them. CoreTip owns strike cleanup.

`BackgroundFrames` applies presentation through `EngineWindow.SetVSyncMode`; the
compatibility VSync setter is a stub. Focus changes retain presentation mode to avoid
swapchain rebuilds. `Patch_ApplicationFocus` supplies `EngineWindow.IsFocused` to the
compatibility getter because its separate app-state field is not synchronized.

Defaults and available modes belong to [Display settings](../docs/src/customization/settings.md#display).
