# Profile settings ownership

`Settings/ModSettings.cs` owns profile preferences that remain usable offline.
Only preferences consumed through the static `Settings` projection need shim
properties. Daemon configuration and endpoint credentials belong to
[the daemon](daemon-config-stores.md).

Every persisted field requires an explicit typed entry in `Fields`. Disk persistence
omits runtime dirty state; failed timer writes retain pending changes for retry.
[Settings apply behavior](ui-settings.md) owns application and save timing.
The alternate temperature label is a profile preference stored separately from
RimWorld's closed temperature enum.

Feature owners are [terminal recall](mod-terminal.md), [Eco](mod-eco.md),
[shared chrome](mod-ui-chrome.md),
and [terminal rendering](mod-terminal-rendering.md) for appearance caches and emoji.

`FramePolicy` owns foreground vSync/FPS pacing and the unfocused 15 FPS cap; Eco
is not a second foreground pacing owner. Temperature-triggered camera/UI sound
overrides belong to `Patches/Options/TemperatureSounds.cs`; map-scoped sounds retain
their original definitions.

When Eco or Grandma mode disables destructive effects, cancel pending effects
rather than deferring them until the mode ends. Strike-specific cleanup stays with CoreTip.
