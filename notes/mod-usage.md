# Mod usage boundary

`Client/Usage/UsageInfo.cs` anchors row ages and reset countdowns to each provider's
last-good poll. Repeated snapshots of the same poll retain the monotonic anchor;
the aggregate failure timestamp does not replace it.

`UI/Settings/UsagePage.cs` merges configured catalog rows and discovered windows.
Unedited discovered rows remain display-only; editing creates a saved override.
[Daemon usage](daemon-usage.md) owns polling and resolved applicability, while
[Settings](ui-settings.md) owns draft/save lifetimes.
