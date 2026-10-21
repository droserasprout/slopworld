# UI Library

`UI/Views/Library/` owns rows and actions. Deferred actions retain typed identity and
availability from catalog snapshots; selection survives filtering. The list viewport
excludes search/details for scrolling and hit testing.

Library's project filter is independent of sidebar filters and applies only to
Projects/Worktrees. All includes future projects; explicit empty selection stays
distinct. Preset categories show user definitions/overrides; system entries stay in
Settings.

`WorktreeInventory` owns requests, retained snapshots, and errors. Refresh/filter
changes invalidate delayed replies; failure retains successful rows. The daemon
supplies Main and readiness state. Shared root-only transport does not imply shared
filter or selection lifetime with the sidebar.

Editors use `DialogSubmitState` for save admission/retry, disabling fields while saving.
Placeholders never become editable values. See [daemon library](daemon-library.md),
[templates](mod-agent-templates.md), and [navigation](mod-sidebar-navigation.md).
User interactions belong to [Library](../docs/src/workspace/library.md).
