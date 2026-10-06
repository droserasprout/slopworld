# Settings apply boundaries

Profile preferences belong to `ModSettings`; machine configuration belongs to the
daemon; RimWorld preferences keep their own lifecycle. Settings > General uses
`ConfigPage`. The palette action Configuration: Edit config.toml opens the raw
`ConfigWindow`, separately from the Settings pages.
General's editable preferences apply live; its footer offers Reload and Edit for
daemon configuration, without Save/Discard controls.

Most profile preferences apply live. Controls mark them dirty for a quiet-period
flush, and Settings closure writes them. UI scale applies on slider release so its
input target remains stable. Persistence ownership belongs to [mod settings](mod-settings.md).

Daemon pages keep independent drafts and save changed fields only. Acknowledgement
accepts submitted values while preserving edits made during the request. Reload
merges unedited fields and reports conflicts; Discard takes the latest remote values.
Stale callbacks cannot overwrite newer outcomes.

Sandbox list editors retain raw text for typing and measurement, including trailing
blank lines; normalized daemon lists are projections of those drafts. Replacing
the selected definition replaces its raw drafts. Renaming a new definition keeps
its drafts and retained field geometry.

Endpoint/raw-editor coordination
belongs to [the client](mod-client.md) and daemon acceptance to
[configuration stores](daemon-config-stores.md).

Code appearance keeps a local draft until Save/Discard. Profile-only theme edits do
not write daemon commands. When commands also change, local preferences commit only
after daemon acceptance. Applied changes can restart active readers while preserving
pins and pane bindings; failed starts retain old readers. User choices belong to
[Settings reference](../docs/src/reference/settings.md), reader lifetime to
[file readers](mod-file-readers.md), and command construction to `PagerCommands`.

Page geometry belongs to `SettingsLayout`. `SettingsPreviewForm` owns retained
scrolling and frame-stable form measurement for Interface, Terminal, and Code;
`SettingsPreviewLayout` owns their pinned/stacked preview geometry.
`SettingsPageLayout.ScrollView` shares padded scroll geometry for About and RimWorld;
each page retains its scroll state and frame-stable content height.
Responsive forms belong to [chrome](mod-ui-chrome.md),
scrolling to [scrolling](mod-ui-scrolling.md), registration/dialogs to
[windows](mod-ui-windows.md), and worker delivery to [workers](daemon-workers.md).
See `CodeAppearanceDraftTests` and `PagerCommandsTests` for local draft/command contracts.

`UnlockUIScale` exempts the vanilla low-resolution scale reset, while the slider
continues applying its value only on release.
