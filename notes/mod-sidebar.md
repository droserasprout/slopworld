# `AgentSidebar` and `ChromeShift`

`AgentSidebar` reuses colonist-bar entries and hit testing but lays them out in
project groups. The back pass draws the panel, headings, tabs, menus and grip before
vanilla consumes input; the front pass draws labels and row actions. A Harmony
finalizer clears `Drawing` if vanilla throws.

Entries keep their original indices for vanilla reordering. Project- or status-filtered and
folded entries are parked off-screen because the colonist bar shares locations for drawing
and hit testing.
`Rows` is the geometry source for labels, portraits, clicks and keyboard order.
Layout reuses revision/geometry keys. Title cleanup caches its text and font inputs with weak
session ownership; font atlas rebuilds invalidate it. Off-screen rows and headings skip repaint
work while input passes retain their control order. Row ages sample the clock once per frame,
and row marks/badges are Repaint-only while row labels, hover checks and tooltips still run.
Files/Git routed sessions are prepared once per draw pass in a reused list; the resulting height
is passed into tree geometry. Per-pass rebuilding includes native-preview and viewer-handoff
changes even when the daemon session revision is unchanged.

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

`WorkspaceLayout` owns the panel rectangle, so the navigation can move to the right without
changing the row model. Fixed chrome uses screen-space panel coordinates; agent rows and
headings remain local to the shared scroll group. The colonist-bar location table is translated
only around external `TryGetEntryAt` calls, and workspace revisions invalidate cached placement
without replacing the active tab, content view, focus, or scroll state.

The agent and host-terminal context menus offer Start/Stop, Terminal and Label; agents also have
Delegate task, Edit, Duplicate, Shell,
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
- In Agents, middle-clicking any session opens the existing stop confirmation while it is active
  and the appropriate removal confirmation once it is down. Right-click remains the row context
  menu for every row.
- Task rows prefer the daemon's optional OpenRouter summary when task summaries are set to
  `once`, and fall back to the bounded one-line task body preview when summaries are disabled,
  too short, unavailable, or still being generated.

Shared views, navigation, filtering, context menus, and vanilla chrome shifts are
covered in [sidebar views and chrome](mod-sidebar-navigation.md).

Sidebar tabs are registered as ordered definitions with stable persisted names, capabilities,
draw/click/action handlers, refresh and fold delegates, and separate close/entered/reselected
lifecycle callbacks. Unknown saved names fall back to Agents. Switching closes menus first,
then closes non-target view state, persists the target, and enters it; reselecting runs only
the target's refresh callback. Files re-entry also initializes Git status, while Git and Tasks
refresh on reselection. Library fetches its catalog on entry, even without socket updates.

- Middle-click closes Files/Git routed headers without confirmation, including restored
  pager/editor sessions, pinned diffs and native Markdown previews. Ordinary durable agent rows
  remain unaffected. Explicit dismissal releases pinned previews; ordinary focus changes still
  preserve them.
- The Tasks list keeps the complete mailbox but virtualizes off-screen rows. Its scroll view
  uses the shared fractional wheel path and thumb dragging.
- Task rows cap their one-line preview before passing it to RimWorld's quadratic `Truncate`; the
  click-through reader uses the maximized content host and a centred bounded panel. It shows
  the complete available dialogue (the original message and latest note) as timestamped
  sender-avatar cards. Message text has its own selection surface, so it supports dragging,
  Ctrl+C, a Copy context-menu action, and a Copy all footer action; long dialogue scrolls inside
  the panel.
