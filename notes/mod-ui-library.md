# UI Library ownership

`UI/Views/Library/` owns catalog rows, details, and actions. `LibraryRowTarget`
retains typed identity and availability for deferred actions; `LibraryRowSelection`
owns selection across filtering. Rows consume captured catalog snapshots. The list
viewport excludes search and details for both scrolling and hit testing.

Library's project filter is independent of the sidebar filter and applies only to
Projects/Worktrees; global definitions remain visible. Preset categories show user
definitions and overrides; system entries remain in Settings.

`WorktreeInventory` owns requests, retained snapshots, and errors. Refresh and
filter changes invalidate delayed replies; failed loads retain successful rows.
The daemon supplies all checkout rows, including Main and its readiness state.
`WorktreeRequests` shares root-only transport with the sidebar catalog, but their
filters and selections have separate lifetimes.

Daemon items belong to [library](daemon-library.md), template forms to
[mod templates](mod-agent-templates.md), and semantic history to
[navigation](mod-sidebar-navigation.md). User interactions belong to the
[Library guide](../docs/src/workspace/library.md).
