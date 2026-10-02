# Settings apply boundaries

Profile preferences belong to `ModSettings`; machine configuration belongs to the
daemon; RimWorld preferences keep their own lifecycle. Settings > General uses
`ConfigPage`. The palette action Configuration: Edit config.toml opens the raw
`ConfigWindow`, separately from the Settings pages.

Most profile preferences apply live. Controls mark them dirty for a quiet-period
flush, and Settings closure writes them. UI scale applies on slider release so its
input target remains stable. Persistence ownership belongs to [mod settings](mod-settings.md).

Daemon pages keep independent drafts and save changed fields only. Acknowledgement
accepts submitted values while preserving edits made during the request. Reload
merges unedited fields and reports conflicts; Discard takes the latest remote values.
Stale callbacks cannot overwrite newer outcomes. Endpoint/raw-editor coordination
belongs to [the client](mod-client.md) and daemon acceptance to
[configuration stores](daemon-config-stores.md).

Code appearance keeps a local draft until Save/Discard. Profile-only theme edits do
not write daemon commands. When commands also change, local preferences commit only
after daemon acceptance. Applied changes can restart active readers while preserving
pins and pane bindings; failed starts retain old readers. User choices belong to
[Settings reference](../docs/src/reference/settings.md), reader lifetime to
[file readers](mod-file-readers.md), and command construction to `PagerCommands`.

Page geometry belongs to `SettingsLayout`, responsive forms to [chrome](mod-ui-chrome.md),
scrolling to [scrolling](mod-ui-scrolling.md), registration/dialogs to
[windows](mod-ui-windows.md), and worker delivery to [workers](daemon-workers.md).
See `CodeAppearanceDraftTests` and `PagerCommandsTests` for local draft/command contracts.

`UnlockUIScale` exempts the vanilla low-resolution scale reset, while the slider
continues applying its value only on release.
