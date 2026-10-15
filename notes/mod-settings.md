# Profile settings ownership

`Settings/ModSettings.cs` owns portable profile data, typed TOML serialization,
normalization, and dirty timing. `ModSettingsStore` owns file loading and replacement
with caller-supplied paths and error reporting. `ProfileSettings` supplies RimWorld's
profile path, fallback path, logging, and the runtime write/flush adapters.
`Settings.cs` projects the active profile through `ModEntry`; only preferences
consumed through that projection need shim properties. Daemon configuration and
endpoint credentials belong to
[the daemon](daemon-config-stores.md).

Every persisted field requires an explicit typed entry in `Fields`. Disk persistence
omits runtime dirty state; failed timer writes retain pending changes for retry.
[Settings apply behavior](ui-settings.md) owns application and save timing.
The alternate temperature label is a profile preference stored separately from
RimWorld's closed temperature enum.
`PreferenceFormats` owns date/time and statusbar preference keys; callers supply
format choices when formatting timestamps. `WorkspacePreferences` owns navigation
side and density keys without depending on workspace geometry. Persisted machine
timestamps keep their wire formats. The clock cache includes both format preferences.

Feature owners are [terminal recall](mod-terminal.md), [Eco](mod-eco.md),
[shared chrome](mod-ui-chrome.md),
and [terminal rendering](mod-terminal-rendering.md) for appearance caches and emoji.

`FramePolicy` owns foreground vSync/FPS pacing and the unfocused 15 FPS cap; Eco
is not a second foreground pacing owner. `BackgroundFrames` applies presentation
changes through `EngineWindow.SetVSyncMode`; the compatibility VSync setter is a stub.
Focus changes adjust only target FPS, retaining the selected presentation mode to avoid
swapchain rebuilds. `Patch_ApplicationFocus` supplies native `EngineWindow.IsFocused`
to the compatibility getter because its separate app-state field is not synchronized. Temperature-triggered camera/UI sound
overrides belong to `Patches/Options/TemperatureSounds.cs`; map-scoped sounds retain
their original definitions.

When Eco or Grandma mode disables destructive effects, cancel pending effects
rather than deferring them until the mode ends. Strike-specific cleanup stays with CoreTip.
