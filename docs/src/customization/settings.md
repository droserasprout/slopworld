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
`MM/DD/YYYY`, and a 24-hour or 12-hour time display. These preferences apply live
to the clock tooltip, task timestamps, jukebox history, and game build date,
and are saved with the profile.

## Storage

Open **Settings > Storage** to inspect private agent state and shared caches,
reset private state, restore trash, or delete unused state. See
[Storage and caches](../maintenance/storage.md) and
[Backup and recovery](../maintenance/backup-and-recovery.md) for the procedures.

## Workers

Open **Settings > Agents > Workers** to customize the shared initial worker prompt.

Use **Edit agent > Workers** to enable delegation, choose allowed templates, and set
independent follow-up, task cancellation, terminal input, and stop/restart permissions
for future workers. Delegation and further delegation are disabled by default.
Templates retain these settings; child workers inherit restrictions from their parent
and template. Existing relationship access is edited separately on the child worker's
Workers tab.

Edit the initial worker prompt in Settings, then save. New workers receive
that prompt; existing workers keep their saved settings. See
[Agent collaboration](../agents/agent-collaboration.md) for delegation and reporting.

## Planner

The chess table button beside Settings on the right of the status bar opens **Planner**.
You can also click the Planner chess table on the map; it arrives in its own drop pod.
Use **Add a task** for quick entry, check a task to complete it, and select a row to
edit its title, priority, due date, projects, or contexts. Quick-add and checkboxes save
immediately; the details pane has **Save changes** and **Discard edits**. Delete removes
only the TODO entry, keeping any linked worker task.

Use the Markdown field directly beneath the task title for long notes, checklists, and code blocks.
**Save changes** saves the task and all edited descriptions. **Discard** restores both
to their last saved values. Failed description saves retain their drafts for retry.
**Preview** renders the current
draft. **Reload description** reads external edits. On a conflict, copy your draft before
discarding edits and reloading. Description drafts survive task and view changes within
the mod session; save before quitting.

Descriptions are limited to 1 MiB and stored as
`planner/descriptions/<sw-id>.md` beneath the daemon data root, alongside `TODO.txt`.
The task line stays ordinary todo.txt with its existing `sw-id:`. Renaming, moving, or
completing a task preserves its description. Deleting a task retains the Markdown file;
restoring the same `sw-id:` reconnects it. Copying a task line requires a new ID and does
not copy its separate description file.

Close task details with the cross button; unsaved edits remain when you reopen the task. The **Projects** dropdown
lets you check multiple existing TODO projects or configured projects.

The list offers My tasks, Today (including overdue), Upcoming, Overdue, Completed, and
All tasks. Search matches titles, projects, and contexts. Sort by due date, priority,
or title. Projects (`+project`) and contexts (`@context`) appear on the left; smaller windows use filter menus and open
task details separately. Worker status and the latest progress note update while Planner
is open.

The daemon stores `TODO.txt` in its application data directory (normally
`~/.local/share/slopworld/`). The **Tasks | Raw** switcher opens the task list or underlying
[todo.txt text](https://github.com/todotxt/todo.txt) for advanced editing. Unknown metadata
and worker links survive task-field edits. Drafts survive view changes within the mod
session; save before quitting. External changes cause a save conflict: copy your draft
from Raw before discarding it and refreshing.

**Links** in task details lists associated Tasks and Workers. Click a Task link to open
its details or a Worker link to focus its terminal. **Link Task…** and **Link Worker…**
attach existing items. Each link has **Remove**; this only removes the association and
never deletes, cancels, or stops the linked item. Removed links stay removed after refresh.
Task outcomes such as Failed belong to the linked Task, while the Planner checkbox
controls whether the backlog item is complete.

**Spawn worker…** lets you choose a project and opens the worker dialog with the task
title and saved Markdown description. Spawning links both the new Task and Worker,
including when worker startup fails. You can remove old links and spawn another attempt.
Save task and description edits before spawning. Links are stored separately at
`planner/links/<sw-id>.json` under the daemon data root.

**Refresh** reloads external TODO edits and links. Due dates and recurrence metadata organize the backlog; they do not
schedule work. Documents are limited to 64 KiB.

Saving adds a stable `sw-id:` to each task. Preserve this and any existing `sw-task:`
metadata. Agents report status through Tasks and cannot edit the Planner document through
its API. Worker completion does not automatically mark the user's line with `x`.
To cancel an unfinished linked worker task, click its Task link and choose **Cancel**.
Cancellation prevents further task updates; it does not stop the worker process or erase
the TODO entry.

## Configuration file

Use the palette's **Configuration: Edit config.toml** action for machine settings,
including fields without a Settings page. The editor hides the daemon token and
validates changes before saving. Projects, agents, and host shells have their own
editors and are preserved when machine settings are replaced.

See [Paths and files](../reference/paths.md#daemon-configuration) for file locations.
