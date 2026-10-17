# Settings apply boundaries

Profile preferences belong to [mod settings](mod-settings.md); machine settings
belong to the daemon; RimWorld preferences keep their own lifecycle. Most profile
controls apply live and mark changes for a quiet-period flush or Settings closure.
UI scale applies on slider release to keep its input target stable; `UnlockUIScale`
bypasses vanilla's low-resolution reset.

Daemon pages keep independent drafts and save changed fields only. Acknowledgement
accepts submitted values while preserving edits made during the request. Reload
merges unedited fields and reports conflicts; Discard takes the latest remote values.
Stale callbacks cannot overwrite newer outcomes. Drafts are scoped by page and
endpoint; saving one page must not reload another. Endpoint changes clear config
paths. Raw editing waits for a successful load, and failed reload preserves text.
Effective previews are daemon-resolved. Connection lifetime belongs to
[the client](mod-client.md); replacement and workspace-record protection belong to
[configuration stores](daemon-config-stores.md).

Code appearance keeps a local Save/Discard draft. Theme-only edits stay local;
when commands also change, profile preferences commit after daemon acceptance.
Reader replacement preserves pins and pane bindings and retains old readers on
failure; [file readers](mod-file-readers.md) owns that lifetime. `PagerCommands`
owns command construction. See `CodeAppearanceDraftTests` and `PagerCommandsTests`.

Sandbox editors retain raw text, including trailing blank lines, for typing and
measurement; normalized daemon lists are projections. Replacing a definition
replaces its drafts, while renaming a new definition preserves drafts and geometry.
`PresetHierarchy` and its tests own tree ordering, including the explicit
`slopworld-debug` exception. Missing parents and cycles must leave presets visible.

`BinaryDiscovery` shares one request generation across host and daemon probes.
Host lookup uses the game's environment; `/api/whereis` uses the daemon's.
`StorageLoadState` publishes shallow inventory before recursive sizes and rejects
callbacks after refresh or disposal. Failed scans preserve paths and navigation;
unknown sizes are not zero. The daemon's `sizes=false` request also skips trash
purging. The manager purges before measured scans; sandbox inventory never deletes.
See `BinaryDiscoveryTests` and `StorageLoadStateTests`.

`SettingsLayout` owns page geometry. `SettingsPreviewForm` and
`SettingsPreviewLayout` retain scrolling and stable measurement, pinning previews
when space permits and scrolling them with the form in short windows. Code keeps
its save footer. Measurement must not invoke controls or setters. Shared geometry
belongs to [chrome](mod-ui-chrome.md), scroll input to [scrolling](mod-ui-scrolling.md),
and page registration/lifetime to [dialogs](mod-ui-windows.md).
User-facing choices belong to the [Settings guide](../docs/src/customization/settings.md).
