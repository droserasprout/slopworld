# Sidebar views and chrome

The core sidebar geometry, selection behavior, and row rendering are in
[mod-sidebar](mod-sidebar.md).

## Views and navigation

Agents, Files, Search, Git, Tasks and Library share the panel, tabs, width, add strip and
input absorption. Their default F1–F6 shortcuts follow that left-to-right order. Switching
tabs preserves preview readers; replacing a preview or explicitly closing it ends the session.
Files/Search/Git park colonist-bar
locations but still build buckets so Alt+number can return to an agent; folding removes
agents from visible order.

## The tab strip

Every view has two possible rows: six tabs plus the project filter, then right-aligned
view controls. Foldable views offer fold/unfold all; Agents has a status visibility menu
(`All`, `Active`, `Idle`, `Down`), Files has dotfiles, and Git has refresh. Search has
dotfiles alone. Views without a control have no second band. `TabH` is the complete strip
height used by both the body and the colonist-bar layout.

The project filter is a set of ticked keys read through `AgentSidebar.Passes`; empty
means all. `[none]` represents unassigned projects, including a real project with that
name. An unknown project key shows nothing rather than falling back to all. Agents,
Files and Library filter while drawing; Search and Git re-request their stored result
when the filter changes.

The Agents status filter treats Working and Waiting as Active. Its visible rows are also
the source for Alt+number and Alt+Z/X navigation; sessions excluded by the selected status
are not appended back into those orders.

The shared add strip offers project and agent editors, a Library submenu for each
library item kind, new sandbox presets and commands, and a host-shell submenu with `~` first,
followed by projects.

The filter menu is multi-select: each tick closes and reopens at its anchor instead of
the cursor. The palette's `View: Filter Projects` uses the same keys and `TickBox`, with
Space toggling without closing.

Context menus run in the back pass because vanilla consumes portrait right-clicks. A
project menu's unsandboxed terminal uses `SessionHub.RunHostShell` and `/api/run` with
`host`; row hover is gated while either `FloatMenu` or `UiMenu` is open without
disabling status-bar click-through.

## Shifting vanilla chrome

`ChromeShift` remaps bottom main-button rects, repositions the inspect pane and shifts
the gizmo grid by the sidebar inset. Inspect tabs need a separate patch because vanilla
draws them outside the window group. Dragging explicitly repositions an already-open
inspect pane; the normal hook runs only on open or resolution change.
