# `AgentSidebar` and `ChromeShift`

`AgentSidebar` reuses colonist-bar entries and hit testing but lays them out in
project groups. The back pass draws the panel, headings, tabs, menus and grip before
vanilla consumes input; the front pass draws labels and row actions. A Harmony
finalizer clears `Drawing` if vanilla throws.

Entries keep their original indices for vanilla reordering. Hidden/folded entries are
parked off-screen because the colonist bar shares locations for drawing and hit testing.
`Rows` is the geometry source for labels, portraits, clicks and keyboard order.

Portrait scale is derived from text: `Nominal` makes the drawn square portrait match the
row's normal three-line height, while compact view reserves only the name and summary;
`RowGap` stays between portraits. The face also has a panel-width limit. Headings, routed
rows and the add strip remain fixed; `Fit` shrinks crowded portraits until their text floor,
then the agent rows and portraits scroll in the remaining body. The body keeps its full
panel width without a scrollbar gutter; overflow is marked by a soft shadow above the fixed
add strip.

Selected portrait corners are queued during the vanilla portrait pass and drawn later in the
same scroll group, using the same local face rect as the portrait. The full view retains
vanilla's bracket texture and selection-jump animation while keeping multi-selection and
caravan selection aligned with the custom face crop during scrolling; compact view omits
the corners and state badge.

### Selection-corner debugging history

`Rows[*].Face` is content-local geometry. `SmoothScroll.Begin` opens a `Widgets.BeginScrollView`
whose outer rect is `Body`; vanilla's `DrawColonist` prefix therefore runs inside that scroll
group. Keep selection rendering in that same group and pass the local face rect directly.

Several tempting fixes were wrong:

- Drawing after `EndScrollView` and subtracting `AgentScroll.Position` applied a second
  coordinate conversion. The corners moved with scrolling but were offset from the portrait.
- Replaying vanilla's private `DrawSelectionOverlayOnGUI` remained unstable. Its
  `Widgets.DrawTextureRotated` call treats a content-local corner as a global rotation pivot,
  so different corners can stay fixed or jump in unrelated directions.

The working solution keeps vanilla selection filtering, `SelectedTexGUI`, selection timestamps
and `CalculateSelectionBracketPositionsUI`. It calculates into a private four-corner array, then
draws each original rotated texture in the active scroll group with its local center converted
by `GUIUtility.GUIToScreenPoint` only for `RotateAroundPivot`. The texture rect stays local. Do
not subtract the scroll position or pass the local center to `Widgets.DrawTextureRotated`.

The portrait prefix replaces vanilla's complete draw, including its icon row. The
front pass adds one badge in full view, sized from the face so it survives shrinking and
anchored to the drawn portrait rather than the cell. `Patch_AgentNeverIdle` remains active
so vanilla does not report an agent idle after the replacement.

Host shells are one-line ghost rows with no pawn/state; durable project-heading shells
stay in that row after their pane stops. Agent rows show the terminal icon and project;
ghost rows emphasize the title/action/file identity and dim context. Viewer, editor and
diff sessions use explicit prefixes because native titles are often `bash` or `less`, and
route to Files/Git. Permanent routed sessions are parked immediately; reconciliation may
otherwise leave their pawn for one tick.

The grip polls `Input.GetMouseButton*`, not IMGUI events: absorbing windows can hide the
initial press and off-screen release. It saves settings on release, owns the panel's
right edge, and keeps tab/add hit gates short of that edge.

## Views and navigation

Agents, Files, Search, Git and Shortcuts share the panel, tabs, width, add strip and
input absorption. Leaving a view closes its readers. Files/Search/Git park colonist-bar
locations but still build buckets so Alt+number can return to an agent; folding removes
agents from visible order.

## The tab strip

Every view has two possible rows: five tabs plus the project filter, then right-aligned
view controls. Foldable views offer fold/unfold all; Agents also has Compact view,
Files has dotfiles, and Git has refresh. Search has dotfiles alone. Views without a control
have no second band. `TabH` is the complete strip height used by both the body and the
colonist-bar layout.

The project filter is a set of ticked keys read through `AgentSidebar.Passes`; empty
means all. `[none]` represents unassigned projects, including a real project with that
name. An unknown project key shows nothing rather than falling back to all. Agents,
Files and Shortcuts filter while drawing; Search and Git re-request their stored result
when the filter changes.

The shared add strip offers project and agent editors, a Shortcuts submenu for each
shortcut kind, new sandbox presets and commands, and a host-shell submenu with `~` first,
followed by projects.

The filter menu is multi-select: each tick closes and reopens at its anchor instead of
the cursor. The palette's `View: Filter Projects` uses the same keys and `TickBox`, with
Space toggling without closing.

Context menus run in the back pass because vanilla consumes portrait right-clicks. A
project menu's unsandboxed terminal uses `SessionHub.RunHostShell` and `/api/run` with
`host`; row hover is gated while either `FloatMenu` or `SlopMenu` is open without
disabling status-bar click-through.

## Shifting vanilla chrome

`ChromeShift` remaps bottom main-button rects, repositions the inspect pane and shifts
the gizmo grid by the sidebar inset. Inspect tabs need a separate patch because vanilla
draws them outside the window group. Dragging explicitly repositions an already-open
inspect pane; the normal hook runs only on open or resolution change.
