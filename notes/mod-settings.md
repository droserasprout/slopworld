# Profile settings

`Settings/ModSettings.cs` owns typed profile data, normalization, and dirty timing;
`ModSettingsStore` owns file replacement with caller-supplied paths/errors.
`ProfileSettings` supplies RimWorld paths and runtime adapters. `Settings.cs` projects
the active profile; only preferences consumed there need shim properties.
[Daemon configuration](daemon-config-stores.md) has separate ownership.

Persisted fields require explicit typed entries in `Fields`. Runtime dirty state stays
off disk, and failed timed writes remain pending. Formatting preferences must not
change machine timestamp formats; caches include the selected formats.
[Settings drafts](ui-settings.md) owns apply/save timing.

`FramePolicy` alone owns foreground and unfocused pacing. `BackgroundFrames` supplies
monitor refresh and applies the policy. VSync can override the FPS selector without
replacing its choice; focus changes adjust target FPS without changing presentation
mode. Eco does not add another foreground pacing policy.

Temperature-triggered camera/UI sounds belong to `Patches/Options/TemperatureSounds.cs`;
map sounds retain their definitions. Disabling destructive effects through Eco or
Grandma cancels pending effects rather than postponing them. CoreTip owns strike cleanup.
