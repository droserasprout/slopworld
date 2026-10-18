# Sidebar navigation

`SidebarTabCatalog` owns order, stable persisted IDs, and capabilities shared with
registration/tests. `SidebarTabRegistry` owns read-only definitions and lookup;
`AgentSidebar` supplies game-bound handlers. Persist IDs, not enum positions or rows.
Unknown saved tabs fall back to Agents.
Shared chrome reserves the tabs/filter row above the optional open-files pane and
the selected tab's action row below it. Every tab reserves the action row, even when
it has no actions, so switching tabs keeps the body at a stable height.
Tab and action targets fill their row height and half the gap to adjacent icons;
glyph and selection-marker bounds stay fixed. Tooltips share the click targets,
menus anchor below the row, and targets exclude the resize grip.

`AgentStatusSelection` shares status checkbox and persistence policy between the
sidebar and command palette. All checks every status; toggles change visibility
individually. An explicit `none` preference keeps an empty selection distinct from All.

`SidebarViewHistory` stores semantic locations. Library targets distinguish template
and ordinary item identity even when names match, resolving against the current
catalog without waiting for refresh. Missing/stale targets can select the tab while
leaving no selected row.

Sidebar tabs, session text rows, fold controls, and filter openers activate on press;
refresh actions activate on release.

Tab changes close menus and view-local input state while preserving readers.
Reselection refreshes without repeating close/enter transitions. Lifetime belongs
to [file readers](mod-file-readers.md) and [Search](mod-ui-search.md).
Scope/path identity belongs to [browse scopes](mod-ui-browse-scopes.md), Library
interaction to [Library](mod-ui-library.md), and filtering/Harmony integration to
[sidebar](mod-sidebar.md).

See [tab definitions](../mod/Source/SlopWorld/UI/Sidebar/SidebarTabDefinition.cs),
[history](../mod/Source/SlopWorld/UI/Sidebar/SidebarViewHistory.cs), and
[tab tests](../mod/Tests/SidebarTabTests.cs).
