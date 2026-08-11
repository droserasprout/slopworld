# `AgentSidebar` and `ChromeShift`

`AgentSidebar` reuses the colonist bar's entries and hit testing but writes their cached
locations into a project-grouped column. Its back pass draws the panel, headings, tabs,
menus, and resize grip before vanilla consumes input; its front pass draws labels and row
actions. `Drawing` must also be cleared by the Harmony finalizer when vanilla throws.

Entries retain their original indices so vanilla reordering remains valid. Hidden or
folded entries are parked off-screen rather than merely omitted because the colonist bar
uses the same locations for drawing and hit testing. `Rows` is the shared geometry for
labels, portraits, clicks, and keyboard order.

Portraits shrink to fit, but font-derived label height, headings, routed rows, and the add
strip do not. `Fit` therefore stops shrinking when the fixed-height labels determine row
pitch. The add strip is reserved at the bottom in every view.

Ephemeral host shells appear as one-line ghost rows and have no pawn or state. Viewer,
editor, and diff sessions are routed to the Files or Git view instead. Routed permanent
sessions must be parked immediately: colony reconciliation may leave their pawn alive for
one tick.

The resize grip polls `Input.GetMouseButton*` rather than relying on IMGUI mouse events.
An absorbing window can prevent the underlying layer from receiving the initial event,
and a release beyond the screen may produce no usable release event. Settings are written
when the drag ends, not on each frame. The grip also owns the panel's right edge — drawn
once, at the end of the pass — and the tab strip and the add strip both hold their press
gates short of it.

## Views and navigation

The Agents, Files, Search, Git, and Shortcuts bodies share the panel, tabs, width, add strip, and
input absorption. Switching away closes readers owned by the old view. Files, Search and Git park
all colonist-bar locations, but buckets are still built so Alt+number can return to an
agent. A fold, unlike a view switch, removes its agents from that visible ordering.

## The tab strip

Two rows. The first is what every view has: the five tabs on the left, the project filter
on the right. The second is what only the current view has — dotfiles for Files and Search,
refresh for Git — right-aligned under the filter, and absent entirely for the two views
that have no such button, so neither wears an empty band. `TabH` is the whole strip, which
is what the body and the colonist-bar layout are pushed down by, so both follow on their
own.

The filter is a set of ticked keys, held one name a line the way the folds are, and every
view is read through `AgentSidebar.Passes`. Empty is all of them, not none. Whatever has no
project of its own is one more key — `[none]` — so it ticks like any other; a project
actually named that shares the line, which is what a sentinel reading the same in the
settings file as in the menu costs. A key no project answers to shows nothing rather than
falling back to all, which is the honest reading while the daemon is still handing its list
over. Agents, Files and Shortcuts read the filter as they draw; Search and Git hold what
they asked the daemon for and are asked again when it changes.

The menu is ticks rather than a pick, so a tick closes it — as every option in a `SlopMenu`
does — and opens it again where it was. That is what the menu's optional anchor is for:
without one it would come back at the cursor and walk across the screen. `View: Filter
Projects` in the palette is the same set of ticks as a sub-list, drawn with the same
`SlopWidgets.TickBox`, where Space ticks without closing.

Context menus run in the back pass because vanilla consumes right-clicks over portraits.
Project menus include an explicitly unsandboxed host terminal; it goes through
`SessionHub.RunHostShell` and `/api/run` with `host` set. Every view routes row hover through
the sidebar-only gate, which goes dark under either a vanilla `FloatMenu` or `SlopMenu`
without disabling the status bar's click-through behavior.

## Shifting vanilla chrome

`ChromeShift` remaps the bottom main-button rects, repositions the inspect pane, and shifts
the gizmo grid by the sidebar inset. Inspect tabs need separate handling because vanilla
draws their tab row outside the window group. Dragging the sidebar explicitly repositions
an already-open inspect pane; its normal positioning hook otherwise runs only on open or
resolution change.
