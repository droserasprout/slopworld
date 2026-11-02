# Settings

Open Settings with the gear icon in the top bar or the **Settings** command in the
palette. Settings opens as tabs over the terminal pane.

## Find a setting

| Category | Controls and guides |
| --- | --- |
| General | Game and daemon controls, date and time formats |
| Display | Fullscreen, frame limits, VSync, smooth scrolling |
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

| Control | Effect |
| --- | --- |
| Fullscreen | Toggle window-manager fullscreen. |
| Frame limiter | Auto (default) follows the display refresh rate. Manual limits are 15, 30, 60, 120, 144, and 240 FPS. |
| VSync (not recommended) | Off by default. Synchronizes presentation with the display, adds scrolling latency, and disables the FPS selector. Turning it off restores the selected limit. |
| Smooth scrolling | Use precise touchpad movement where supported; disable it for wheel steps. Enabled by default. |

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
calculations and the colony clock. Due dates remain calendar dates. Timer schedules
still use UTC; their last and next occurrence timestamps use the selected zone.

## Storage

Open **Settings > Storage** to inspect private agent state and shared caches,
reset private state, restore trash, or delete unused state. See
[Storage and caches](../maintenance/storage.md) and
[Backup and recovery](../maintenance/backup-and-recovery.md) for the procedures.

## Workers

Open **Settings > Agents > Workers** to edit and save the initial worker prompt.
Changes apply to new workers.

Configure delegation, allowed templates, and parent permissions in **Edit agent > Workers**.
See [Agent collaboration](../agents/agent-collaboration.md#parent-worker-access).

## Planner

Open **Planner** with the chess table button beside Settings or the chess table on the map.
Use **Add a task** for quick entry, check a task to complete it, and select a row to
edit its title, priority, due date, projects, or contexts. Quick-add and checkboxes save
immediately. The details pane uses **Save changes** and **Discard edits**.

The list filters by date, completion, project (`+project`), and context (`@context`).
Search matches titles, projects, and contexts; sort by due date, priority, or title.
The **Projects** dropdown accepts multiple TODO or configured projects. Smaller windows
use filter menus and open details separately. Due dates and recurrence metadata do not
schedule work.

Use the Markdown field beneath the title for notes, checklists, and code blocks.
**Preview** renders the draft. **Save changes** saves the task and all edited descriptions;
failed saves retain drafts for retry. **Discard** restores the last saved values.
Drafts survive task and view changes within the mod session; save before quitting.

**Tasks | Raw** switches between the task list and its underlying
[todo.txt text](https://github.com/todotxt/todo.txt). Field edits preserve unknown metadata.
**Refresh** reloads external TODO edits and links; **Reload description** reads external
Markdown edits. On a conflict, copy the affected draft before discarding and reloading.

**Links** lists associated Tasks and Workers, with live status and progress. Click a
Task to open its details or a Worker to focus its terminal. **Link Task…** and
**Link Worker…** attach existing items; **Remove** detaches them without deleting,
canceling, or stopping them. Worker outcomes do not change the Planner checkbox.

Save edits before choosing **Spawn worker…**. Select a project, then use the worker
dialog, prefilled with the task title and saved description. Spawning links the new
Task and Worker even if startup fails. You can remove old links and spawn another worker.
To cancel an unfinished task, open its link and choose **Cancel**. Cancellation prevents
further task updates but leaves the worker process running.

The daemon stores `TODO.txt` (up to 64 KiB) in its data directory, normally
`~/.local/share/slopworld/`. Saving assigns each task a stable `sw-id:`; preserve it and
any `sw-task:` metadata when editing Raw. Agents report through Tasks and cannot edit
the backlog through the Planner API.

Descriptions live in `planner/descriptions/<sw-id>.md` (up to 1 MiB each); links live in
`planner/links/<sw-id>.json`. Renaming, moving, or completing a task preserves its description.
Deleting a TODO entry keeps its description and linked worker task. Restoring the same
`sw-id:` reconnects the description; copying a task requires a new ID and does not copy
its description.

## Configuration file

Use the palette's **Configuration: Edit config.toml** action for machine settings,
including fields without a Settings page. The editor hides the daemon token and
validates changes before saving. Projects, agents, and host shells have their own
editors and are preserved when machine settings are replaced.

See [Paths and files](../reference/paths.md#daemon-configuration) for file locations.
