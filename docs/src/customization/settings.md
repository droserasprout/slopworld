# Settings

Open Settings with the gear icon in the top bar or the **Settings** command in the
palette. Settings opens as tabs over the terminal pane.

## Find a setting

| Category | Controls and guides |
| --- | --- |
| General | Game and daemon controls, date and time formats |
| Display | Fullscreen, screensaver inhibition, frame limits, VSync, smooth scrolling |
| Appearance | Interface styling, workspace layout, terminal appearance, code readers |
| Integrations | [Credentials and usage polling](../agents/usage-and-summaries.md) |
| Agents | [Prompt summaries](../agents/usage-and-summaries.md#prompt-summaries) and [workers](#workers) |
| Sandbox | [Sandbox presets](../sandbox/sandbox-presets.md) |
| Storage | [Private state and shared caches](../maintenance/storage.md) |
| Commands | Defaults, [command presets](../workspace/command-presets.md), binary discovery |
| Keyboard | [Key bindings](../reference/keyboard-shortcuts.md) |
| Audio | [Jukebox and audio sources](jukebox.md) |
| RimWorld | Game options |
| About | Project information |

## Applying changes

| Change | When it applies |
| --- | --- |
| Most profile, game, and audio controls | Live; preferences are saved automatically. |
| Code appearance | After Save; restart active readers to use the new settings. |
| Daemon forms | After Save validates and applies the submitted values. |
| Agent command, network, DNS, limits, sandbox, and project mounts | Next agent start; restart a running agent. |
| Listener address and other startup settings | After daemon restart. |

Editors have separate Save actions. **Discard** abandons the current form's edits.
While the daemon is running, use Settings, the configuration editor, or the API
for machine settings and workspace records. To edit their files externally, stop
the daemon first; those files load at startup. Library items and preset files
reload while the daemon runs.

## On exit

Under **General > Game**, open **On exit** to toggle its checkboxes:

- **Ask** (on by default): confirm before quitting from GM, Alt+F4, or window close.
- **Stop radio** (on by default): stop jukebox playback when quitting. This is the
  same preference shown in Audio and the jukebox menu.
- **Stop active sessions** (off by default): stop running session processes before
  quitting, without deleting configured sessions.

With **Ask** unchecked, quitting applies the selected actions immediately. If
sessions cannot be stopped, choose **Retry** or **Quit anyway**, or press Escape
to stay in SlopWorld. Quit saves the colony through the usual shutdown path.
When **Stop active sessions** is checked, the confirmation lists the currently
active sessions. Check **Don't ask again** and choose **Quit** to turn off **Ask**;
cancelling leaves the setting unchanged.

## Display

Open **Settings > Display** for fullscreen, frame limits, VSync, and scrolling.
On Linux, the default launcher window override uses X11 or XWayland and keeps the
renderer windowed. **Display > Fullscreen** and F11 control fullscreen through the
window manager; the window stays undecorated when fullscreen is off. Native
RimWorld display modes are hidden.
`--no-window-fix` skips the launcher’s X11 selection.

| Control | Effect |
| --- | --- |
| Fullscreen | Toggle fullscreen; uses the window manager with the Linux window override. |
| Frame limiter | Auto (default) follows the display refresh rate. Manual limits are 15, 30, 60, 120, 144, and 240 FPS. |
| VSync (not recommended) | Off by default. Synchronizes presentation with the display, adds scrolling latency, and disables the FPS selector. Turning it off restores the selected limit. |
| Smooth scrolling | Use precise touchpad movement where supported; disable it for wheel steps. Enabled by default. |

**Post-processing** adds visual effects such as scanlines and vignette. It is off by default.

**Window > Inhibit screensaver** keeps the screen awake while SlopWorld
is running, including in menus and in the background. It is enabled by default,
applies live, and is saved with the profile. Turn it off to restore normal desktop
idle behavior.

Unfocused windows use 15 FPS and restore foreground pacing on focus.
Auto uses the nearest integer refresh rate and falls back to 60 FPS if unavailable.
These preferences apply live and are saved with the profile.
[Eco mode](../workspace/gameplay.md#eco-mode) controls colony simulation separately.

## Appearance

| Page | Controls |
| --- | --- |
| Interface | UI scale, fonts, colors, cursor styling |
| Workspace | Density, sidebar, statusbar |
| Terminal | Terminal appearance |
| Code | Pager, syntax highlighter, code themes |

Under **Appearance > Code**, **Auto** selects tools installed on the daemon host:

- Pager: moor → bat → less → more.
- Highlighter: bat → Pygments → highlight → plain text.

Explicit tools and custom commands stay fixed. **Off** disables highlighting.
Moor is optional and preferred by Auto when installed on the daemon host. It preserves diff
backgrounds during horizontal scrolling. File views stream the selected highlighter
into moor and retain source numbering and line navigation.
When Auto selects bat as the pager, bat uses less or more to page its output;
without either, it prints directly. Save and restart active readers after a change.

## Date and time

Under **General > Locale**, choose `YYYY-MM-DD` (default), `DD/MM/YYYY`, or
`MM/DD/YYYY`, a 24-hour or 12-hour time display, and a time zone. **Auto** uses
the game desktop’s zone, even with a remote daemon. Choose a named zone to override it.
Changes apply live to clocks and timestamps and are saved with the profile.
Date formatting also applies to the game build date. The time zone also sets Planner’s Today and overdue
calculations and the colony clock. Due dates remain calendar dates.
[Calendar timers](../workspace/library.md#timers) use their saved time zone; this
preference supplies the initial zone for new timers and displays occurrence
timestamps without changing existing schedules.

## Storage

Open **Settings > Storage** to inspect private agent state and shared caches,
reset private state, restore trash, or delete unused state. See
[Storage and caches](../maintenance/storage.md) and
[Backup and recovery](../maintenance/backup-and-recovery.md) for the procedures.

## Workers

Open **Settings > Agents > Workers** to edit and save the initial worker prompt.
Changes apply to new workers.

Configure delegation, allowed templates, and current agent permissions in **Edit agent > Permissions**.
See [Agent collaboration](../agents/agent-collaboration.md#parent-worker-access).

## Planner

Open Planner with the chess table button beside Settings or the chess table on the map.
See [Planner](../workspace/planner.md) for the backlog, notes, and worker assignments.

## Configuration file

Use the palette's **Configuration: Edit config.toml** action for machine settings,
including fields without a Settings page. The editor hides the daemon token and
validates changes before saving. Projects, agents, and host shells have their own
editors and are preserved when machine settings are replaced.

See [Paths and files](../reference/paths.md#daemon-configuration) for file locations.
