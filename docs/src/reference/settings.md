# Settings

Open Settings with the gear icon in the top bar or the **Settings** command in the palette.
Settings opens as tabs over the terminal pane.

## Find a setting

| Category | Pages or purpose |
| --- | --- |
| General | Game and daemon controls |
| Display | Fullscreen, frame pacing, smooth scrolling |
| Appearance | Interface, Workspace, Terminal, Code |
| Integrations | Credentials, Usage |
| Agents | Summaries, Workers |
| Sandbox | Sandbox presets |
| Storage | Private state and shared caches |
| Commands | Defaults, Apps, Binaries |
| Keyboard, Audio, RimWorld, About | Key bindings, audio sources, game options, project information |

See [Configuring agents](../guides/configuring-agents.md),
[Configuring sandboxes](../guides/configuring-sandboxes.md), and
[Integrations and commands](integrations.md) for detailed controls. See [Jukebox](../guides/jukebox.md) for audio source setup.

**Display**, immediately after General, contains fullscreen, frame pacing, and smooth scrolling.

On Linux, the launcher supplies `-popupwindow -screen-fullscreen 0 -force-opengl`.
Use `slopworld --no-window-fix` to omit those default arguments. The mod's Linux
window-manager hook still follows the saved fullscreen setting.
**Appearance > Interface** contains UI scale, fonts, colors, and cursor styling.
**Appearance > Workspace** contains density, sidebar and statusbar controls.

The **Storage** page lists private agent state and shared caches. Use it to reset private state,
restore trash, or delete unused state. See [Backup and recovery](../guides/backup-and-recovery.md).

Worker settings are under **Settings > Agents > Workers**. Add templates to the worker
allowlist so agents can use them to create workers. New templates do not enter the allowlist
automatically. Edit the initial worker prompt on this page. New workers receive the saved
prompt. The Worker menu can use any catalog template. Allowlist changes do not affect
existing workers.

## Display

Smooth scrolling is enabled by default and uses precise touchpad movement where
supported; disable it for wheel steps. Changes apply live and are saved with the profile.
Frame pacing offers VSync or 15, 30, 60, 120, 144, and 240 FPS presets. FPS limits
disable VSync. Unfocused windows use 15 FPS and restore foreground pacing on focus.

## Configuration file

The palette's **Configuration: Edit config.toml** action opens machine settings,
including fields without a Settings page. Projects, agents and host shells use
their own editors and are preserved when settings are replaced. Inline workspace
sections are rejected. The editor hides the daemon token.
Before it saves, it parses and checks the replacement. Invalid replacements are rejected.

Worker configuration uses `[daemon.instructions].worker_prompt` and
`[daemon].worker_templates`. The prompt can refer to `$SLOPWORLD_TASK_ID`.

## Applying changes

**Appearance > Code** offers Auto for the pager and syntax highlighter. Auto is the
default for new configurations and selects tools installed on the daemon host:

- Pager: bat → less → more.
- Highlighter: bat → Pygments (`pygmentize`) → highlight → plain text.

Explicit tool and custom command choices stay fixed. Selecting Off disables the
highlighter. When Auto selects bat as the pager, its child pager uses less or more;
without either, bat prints directly. Save and restart active readers to apply a change.

| Change | When it applies |
| --- | --- |
| Most profile, game, and audio controls | Live; preferences are saved automatically. |
| Code appearance | After Save; Discard abandons the draft. |
| Daemon forms | Save checks and applies submitted values; Discard abandons edits. |
| Agent command, network, DNS, limits, sandbox, and project mounts | Next agent start; restart a running agent. |
| Listener address and other startup settings | After daemon restart. |

The native daemon listener defaults to `127.0.0.1:7717`.
Valid external edits to `config.toml` reload automatically; invalid edits leave
active settings in place. Preset, agent, project, and Library editors have
separate Save actions.
