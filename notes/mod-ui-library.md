# UI Library ownership

`UI/Views/Library/` owns catalog rows, details, and actions. Clicking a row selects
it; the details area controls editing/execution. The list viewport excludes search
and details for both scrolling and hit testing. Typed row variants own their catalog
snapshots and presentation in `LibraryView.Rows.cs`. `LibraryRowTarget` owns identity
and action availability, retaining typed targets for deferred actions. `LibraryRowSelection`
owns click decisions and selection lifetime across filtering; `LibraryView.Effects.cs`
translates typed requests into game effects. Details are built only for the selected
row; tooltips are built only on hover.

Global definitions remain visible under project filtering. Catalog groups precede
expandable Projects, Worktrees, Sandbox presets, and App presets. Library's project
filter is independent of the global sidebar filter and applies to Projects/Worktrees.
Preset categories show user definitions/overrides; system entries remain in Settings.
Project creation belongs to the shared `+` menu.

`WorktreeInventory` owns Library worktree requests, retained snapshots, and errors;
row construction only consumes those snapshots. Refresh and project-filter changes
invalidate delayed replies, while failed loads retain the last successful rows.
The daemon supplies every checkout row, including Main and its readiness state.
`WorktreeRequests` shares root-only transport with the sidebar catalog; Library
filtering and sidebar choices retain separate lifetimes.

Daemon item ownership belongs to [library](daemon-library.md), template forms to
[mod templates](mod-agent-templates.md), and semantic history to
[navigation](mod-sidebar-navigation.md).
