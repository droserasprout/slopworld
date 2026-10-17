# Mod usage boundary

`Client/Usage/UsageInfo.cs` anchors row ages and reset countdowns to each provider's
last-good poll. Repeated snapshots of the same poll retain the monotonic anchor;
the aggregate failure timestamp does not replace it.

`UI/Settings/UsagePage.cs` merges configured catalog rows and discovered windows.
Unedited discovered rows remain display-only; editing creates a saved override.
The profile's global statusbar visibility defaults on; daemon polling defaults off.
The statusbar requires global visibility and an enabled daemon row. Missing provider
credentials remove its rows without removing the settings catalog. Visibility does
not change polling, and there are no per-row visibility preferences.
[Daemon usage](daemon-usage.md) owns polling and resolved applicability, while
[Settings](ui-settings.md) owns draft/save lifetimes.
