# Settings vocabulary and apply behavior

## Names

Use **Settings** for the user-facing umbrella: the main button, top-bar door,
content-view title, command-palette category, and pages such as Settings >
Appearance.

Keep **Configuration** for the machine-wide daemon file and its direct editor:
`config.toml`, `GET/PUT /api/config`, `PUT /api/config/patch`, `Config`, and
`DaemonConfig`. The command-palette action that opens the raw editor is therefore
`Configuration: Edit config.toml`; it is editing a file, not browsing a settings
page.

Keep **Options** only where it is a RimWorld or implementation contract:
`Dialog_Options`, `OptionCategoryDef`, `OptionListingUtility`, `OptionsView`,
and `ModOptions`. Renaming those would make the code less recognizable without
changing the user-facing vocabulary. `ModSettings` and RimWorld's
`SettingsCategory` likewise retain their API names.

## Apply behavior

“Reactive” means the running game or daemon starts using the new value without
an explicit confirmation dialog. Persistence and application are separate:
some values apply live but are written when the Settings view closes.

| Scope | Values | Apply and write behavior |
| --- | --- | --- |
| Mod/UI, live and written on interaction | Sidebar width, project folds, sidebar tab/filter/hidden state, usage icon choices, radio station/mute/stop-on-exit, cursor choice | Apply immediately; write the mod settings file on the interaction's release/click. |
| Mod/UI, live and written on Settings close | Auto-connect, usage spent/left display, terminal font/size/theme/cursor color, UI scheme/font/size, cursor grayscale/debug marker, status-bar visibility, Grandma mode, Eco mode/dimming | The current screen or simulation reads the changed value immediately; `ModSettings.Write` runs when Settings closes. There is no per-field Cancel. |
| RimWorld-owned, live and written by RimWorld | Master/game/music/ambient/UI volume, UI scale, tiny-font preference, and temperature unit | The engine responds immediately. UI scale is written after the slider is released; the other preferences follow RimWorld's Settings lifecycle. |
| Daemon settings, explicit Save | Usage rows and credentials, polling, agent/task summary policies, model and prompt, worker instructions, and the Commands page's preset/app templates | Pages stage edits locally. Save sends a partial patch, validates it, updates the daemon, and rereads the pages. The daemon reloads ordinary live values; startup-only values still need a daemon restart. |
| Raw daemon configuration, explicit Save | The complete redacted `config.toml` text, including fields not represented by the GUI | Save sends the replacement text. TOML parsing and validation happen before the file is replaced. |
| Presets, agents, projects, library items | Durable daemon objects rather than simple settings | Each editor has its own Save/apply action. Existing agent processes are not silently rebuilt from changed defaults, projects, or sandbox presets. |

The listener bind address remains available through the raw configuration editor.

General > Experimental stages `daemon.experimental_breadcrumbs` and
`daemon.experimental_instructions` (both default false); Save applies them. Breadcrumbs
gates breadcrumb delivery and its agent/project controls. Instructions gates SLOPWORLD.md
generation/mounting, its agent control, and the Instructions editor/preview. Gated controls
stay visible but greyed out, and preferences remain stored while disabled. Generated
instruction discovery needs both switches. Pending injection is cancelled immediately;
existing mounts last until agent restart. Settings > Integrations > Workers owns the worker
prompt (Instructions gate).

Confirmation dialogs are reserved for destructive or externally consequential
operations: killing/removing agents or projects, and resetting, restoring, or
deleting private state. Reversible appearance,
display, audio, polling, and mode switches do not need a modal confirmation;
their reactive preview plus the page's Save/close behavior is the confirmation
boundary.
