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
when the drag ends, not on each frame.

## Views and navigation

The Agents, Files, Git, and Shortcuts bodies share the panel, tabs, width, add strip, and
input absorption. Switching away closes readers owned by the old view. Files and Git park
all colonist-bar locations, but buckets are still built so Alt+number can return to an
agent. A fold, unlike a view switch, removes its agents from that visible ordering.

Context menus run in the back pass because vanilla consumes right-clicks over portraits.
Project menus include an explicitly unsandboxed host terminal; it goes through
`SessionHub.RunHostShell` and `/api/run` with `host` set.

## Shifting vanilla chrome

`ChromeShift` remaps the bottom main-button rects, repositions the inspect pane, and shifts
the gizmo grid by the sidebar inset. Inspect tabs need separate handling because vanilla
draws their tab row outside the window group. Dragging the sidebar explicitly repositions
an already-open inspect pane; its normal positioning hook otherwise runs only on open or
resolution change.
