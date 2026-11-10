# Settings

Open Settings with the gear icon in the top bar or the **Settings** command in the
palette. Settings opens as tabs over the terminal pane.

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

Closing the game can leave agents running on the daemon host. Use
**General > Game > On exit** to choose whether quitting also stops sessions and
radio playback. Stopping sessions does not delete their configuration.

If a session cannot be stopped, you can retry, quit anyway, or cancel quitting.
Disabling exit confirmation makes the selected actions immediate. The colony
still saves through its usual shutdown path.

## Display

Open **Settings > Display** for fullscreen, frame limits, VSync, and scrolling.
On Linux, the default launcher window override uses X11 or XWayland and keeps the
renderer windowed. **Display > Fullscreen** and F11 control fullscreen through the
window manager; the window stays undecorated when fullscreen is off. Native
RimWorld display modes are hidden.
`--no-window-fix` skips the launcher’s X11 selection.

Leave the frame limiter on **Auto** to follow the monitor's refresh rate, or
lower it to reduce rendering work. VSync synchronizes presentation with the
display but can add scrolling latency; it takes precedence over the selected
frame limit. Turning VSync off restores that limit. Unfocused windows use a
fixed background frame rate and restore your selection on focus.

The limit controls rendering. Keyboard and mouse events, daemon communication,
and game updates continue between rendered frames. A low limit makes visual
feedback less frequent, but does not pace typing or send one character per frame.

Disable **Smooth scrolling** if you prefer discrete wheel steps. SlopWorld keeps
the desktop awake while it runs, even in the background; turn off
**Window > Inhibit screensaver** to allow normal desktop idle behavior.

Display preferences apply live and are saved with the profile.
[Eco mode](../workspace/gameplay.md#eco-mode) controls colony simulation separately.

## Appearance

Under **Appearance > Code**, **Auto** selects from tools installed on the daemon
host. Installing or removing tools can change that selection; choose an explicit
tool or custom command when you need it to stay fixed.

Moor is optional and preferred by Auto when installed on the daemon host. It preserves diff
backgrounds during horizontal scrolling. File views stream the selected highlighter
into moor and retain source numbering and line navigation.
When Auto selects bat as the pager, bat uses less or more to page its output;
without either, it prints directly. Save and restart active readers after a change.

## Date and time

Set date and time formatting under **General > Locale**. The **Auto** time zone uses
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
