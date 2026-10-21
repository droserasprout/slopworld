# Sidebar navigation

`SidebarTabCatalog` owns order, stable persisted IDs, and capabilities;
`SidebarTabRegistry` owns lookup; `Sidebar` supplies game-bound handlers. Persist IDs,
never enum positions/rows. Unknown tabs fall back to Agents.

Every tab reserves its action row so switching keeps body height stable. Drawing,
input, and tooltips share full-height targets; targets exclude the resize grip.
Navigation/folds/filter menus activate on press, refresh actions on release.

`AgentStatusSelection` shares sidebar/palette filtering. Explicit none remains
distinct from All. `SidebarViewHistory` stores semantic locations, distinguishing
templates from ordinary library items even with matching names. Resolve against the
current catalog; stale targets may select a tab without a row.

Tab changes close menus and local input while preserving readers. Reselection
refreshes without repeating close/enter transitions. See [readers](mod-file-readers.md),
[Search](mod-ui-search.md), [browse scopes](mod-ui-browse-scopes.md), and
[Library](mod-ui-library.md).

`CreationActions` owns the shared creation tree, lazy template/project choices, and
launch callbacks. The sidebar's **+** menu and the palette's **Add…** command
adapt that tree to their own rows. Worker and host-shell choices respect the shared
project filter. Palette submenus retain parent searches and
selection while descending and returning through breadcrumbs.

Start with [tab definitions](../mod/Source/SlopWorld/UI/Sidebar/SidebarTabDefinition.cs),
[history](../mod/Source/SlopWorld/UI/Sidebar/SidebarViewHistory.cs), and
[tab tests](../mod/Tests/SidebarTabTests.cs).
