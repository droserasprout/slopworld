# Sidebar navigation

`SidebarTabCatalog` owns order, stable persisted IDs, and capabilities shared with
registration/tests. `SidebarTabRegistry` owns read-only definitions and lookup;
`AgentSidebar` supplies game-bound handlers. Persist IDs, not enum positions or rows.
Unknown saved tabs fall back to Agents.

`SidebarViewHistory` stores semantic locations. Library targets distinguish template
and ordinary item identity even when names match, resolving against the current
catalog without waiting for refresh. Missing/stale targets can select the tab while
leaving no selected row.

Tab changes close menus and view-local input state while preserving readers.
Reselection refreshes without repeating close/enter transitions. Lifetime belongs
to [file readers](mod-file-readers.md) and [Search](mod-ui-search.md).
Scope/path identity belongs to [browse scopes](mod-ui-browse-scopes.md), Library
interaction to [Library](mod-ui-library.md), and filtering/Harmony integration to
[sidebar](mod-sidebar.md).

See [tab definitions](../mod/Source/SlopWorld/UI/Sidebar/SidebarTabDefinition.cs),
[history](../mod/Source/SlopWorld/UI/Sidebar/SidebarViewHistory.cs), and
[tab tests](../mod/Tests/SidebarTabTests.cs).
