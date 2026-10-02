# UI Library ownership

`UI/Views/Library/` owns catalog rows, details, and actions. Clicking a row selects
it; the details area controls editing/execution. The list viewport excludes search
and details for both scrolling and hit testing.

Global definitions remain visible under project filtering. Catalog groups precede
expandable Projects, Worktrees, Sandbox presets, and App presets. Library's project
filter is independent of the global sidebar filter and applies to Projects/Worktrees.
Preset categories show user definitions/overrides; system entries remain in Settings.
Project creation belongs to the shared `+` menu.

Daemon item ownership belongs to [library](daemon-library.md), template forms to
[mod templates](mod-agent-templates.md), and semantic history to
[navigation](mod-sidebar-navigation.md).
