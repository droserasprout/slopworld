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

Compact agent rows can put the optional effective startup/network flags (`a`, `r`, `h`) at
the right of their second line; the Appearance setting controls them. Host shells are
one-line ghost rows with no pawn/state; durable project-heading shells
stay in that row after their pane stops. Agent rows show the terminal icon and project;
ghost rows emphasize the title/action/file identity and dim context. Viewer, editor and
diff sessions use explicit prefixes because native titles are often `bash` or `less`, and
route to Files/Git. Permanent routed sessions are parked immediately; reconciliation may
otherwise leave their pawn for one tick.

The grip polls `Input.GetMouseButton*`, not IMGUI events: absorbing windows can hide the
initial press and off-screen release. It saves settings on release, owns the panel's
right edge, and keeps tab/add hit gates short of that edge.

The agent context menu offers Start/Stop, Terminal, Label, Edit, Duplicate, Shell,
Remove, and New look. Shell spawns an ephemeral shell errand that clones the agent's
sandbox config (presets, network, dns, limits, mounts) via the `like` field on
`/api/run`, so the shell sees the same filesystem as the agent.

Shared views, navigation, filtering, context menus, and vanilla chrome shifts are
covered in [sidebar views and chrome](mod-sidebar-navigation.md).
