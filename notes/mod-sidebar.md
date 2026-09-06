# `AgentSidebar` and `ChromeShift`

`AgentSidebar` reuses colonist-bar entries and hit testing but lays them out in
project groups. The back pass draws the panel, headings, tabs, menus and grip before
vanilla consumes input; the front pass draws labels and row actions. A Harmony
finalizer clears `Drawing` if vanilla throws.

Entries keep their original indices for vanilla reordering. Project- or status-filtered and
folded entries are parked off-screen because the colonist bar shares locations for drawing
and hit testing.
`Rows` is the geometry source for labels, portraits, clicks and keyboard order.

Portrait scale is derived from text: `Nominal` makes the drawn square portrait match the
row's normal three-line height, while compact view reserves only the name and summary;
`RowGap` stays between portraits. The face also has a panel-width limit. Headings, routed
rows and the add strip remain fixed; `Fit` shrinks crowded portraits until their text floor,
then the agent rows and portraits scroll in the remaining body. The body keeps its full
panel width without a scrollbar gutter; overflow is marked by a soft shadow above the fixed
add strip.

Agent project headings remain foldable and show the project's active/total agent count
right-aligned in both folded and unfolded states. Active means Working or Waiting, matching
the Agents status filter; host, ephemeral and task-worker rows are not counted as agents.

Selected portrait corners are queued during the vanilla portrait pass and drawn later in the
same scroll group, using the same local face rect as the portrait. The full view retains
vanilla's bracket texture and selection-jump animation while keeping multi-selection and
caravan selection aligned with the custom face crop during scrolling; compact view omits
the corners and state badge.

The portrait prefix replaces vanilla's complete draw, including its icon row. The
front pass adds one badge in full view, sized from the face so it survives shrinking and
anchored to the drawn portrait rather than the cell. `Patch_AgentNeverIdle` remains active
so vanilla does not report an agent idle after the replacement.

Compact agent rows can put the optional effective startup/network flags (`a`, `r`, `h`) at
the right of their second line; the Appearance setting controls them. Host shells are
one-line ghost rows with no pawn/state; durable project-heading shells
stay in that row after their pane stops. Agent rows show the terminal icon and project;
ghost rows emphasize the title/action/file identity and dim context. Viewer, editor and
diff sessions use explicit prefixes because native titles are often `bash` or `less`, and
route to Files/Git. Permanent routed sessions are parked immediately; reconciliation may
otherwise leave their pawn for one tick.
Host terminal titles are white while a foreground process is running, grey at a shell prompt,
and red when the terminal is down.

The grip polls `Input.GetMouseButton*`, not IMGUI events: absorbing windows can hide the
initial press and off-screen release. It saves settings on release, owns the panel's
right edge, keeps tab/add hit gates short of that edge, and sends the measured pane shape with
the background redraw request so every live tmux pane is ready before an inactive tab opens.

The agent context menu offers Start/Stop, Terminal, Label, Delegate task, Edit, Duplicate, Shell,
Storage, Remove, and New look. The Tasks tab lists the complete root task board, refreshes it through the
daemon, filters by status/direction/agent, and opens task detail/status actions; plain clicks open
task detail, Ctrl+Click toggles task rows, and Shift+Click selects a visible range. Remove applies
to all selected terminal tasks, while Cancel marks selected queued or accepted tasks as canceled.
Canceled tasks can then be removed like other finished tasks. Storage resolves the
agent's active private-state entry through the
same daemon inventory as Settings and opens it as the focused root of Files. Shell spawns an
ephemeral shell errand that clones the agent's
sandbox config (presets, network, dns, limits, mounts) via the `like` field on
`/api/run`, so the shell sees the same filesystem as the agent.

Shared views, navigation, filtering, context menus, and vanilla chrome shifts are
covered in [sidebar views and chrome](mod-sidebar-navigation.md).
- The Tasks list keeps the complete mailbox but virtualizes off-screen rows. Its scroll view
  deliberately avoids Linux XInput precision polling; Unity wheel events and thumb dragging are
  sufficient here and avoid a severe frame-time regression on some X11 systems.
- Task rows cap their one-line preview before passing it to RimWorld's quadratic `Truncate`; the
  click-through reader uses the maximized content host and a centred bounded panel. It shows
  the complete available dialogue (the original message and latest note) as timestamped
  sender-avatar cards. Message text has its own selection surface, so it supports dragging,
  Ctrl+C, a Copy context-menu action, and a Copy all footer action; long dialogue scrolls inside
  the panel.
