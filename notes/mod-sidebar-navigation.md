# Sidebar navigation

`SidebarTabRegistry` is the ordered definition table.
`SidebarViewHistory` stores semantic back/forward targets. Persist stable tab IDs, not enum positions or reconstructed row objects.
Unknown tabs select Agents instead.
Stale history targets may select a tab without a row.
Library targets distinguish templates from ordinary entries even when names match.
Library selection resolves catalog identity after refresh.
Row clicks select entries. The details area controls execution and editing. Its list viewport excludes search and details for both scrolling and
hit testing. Global Library definitions remain visible under project filters. The Library lists
its catalog groups first, then expandable Projects, Worktrees, Sandbox presets, and App presets
categories.

The second sidebar row has an independent Library project filter.
Projects and Worktrees use it without changing the global sidebar filter. The preset categories list user
entries and overrides, while system entries stay in Settings. Every category entry selects into
the shared details/action area. Project creation belongs to the shared `+` menu, not the Library
category.

Tab changes close menus and view-local input state, but preserve preview readers. Reselection
refreshes without repeating close/enter lifetime changes. Files also primes Git's shared
change lookup. Reader ownership: [Files](mod-ui-files.md), [Git](mod-ui-git.md),
[Search](mod-ui-search.md).

An empty project-filter set means all, while unknown saved keys mean no match. Filtering is
part of keyboard order as well as drawing: excluded agents must not reappear in cycling.

Vanilla main buttons, inspect panes, gizmos and colonist hit tests use different coordinate
paths. `ChromeShift` and the colonist-bar patches must all use the workspace inset.
Changing only the drawn sidebar leaves invisible hit targets in old positions.

`SidebarScopes` adapts the game-free `BrowseScopeCatalog` into one catalog/selection owner for
Files, Git, Search and the global filter menu. Child preferences are independent of the
project-name filter and default to Main only. Internal navigation keys contain stable project
and worktree IDs. Never send these keys as project names to the daemon. `SessionStore` resolves
them at the request boundary. Catalog failures retain the previous choices and records, while
successful removal drops tree nodes. At most two catalog reads run at once. Generation checks reject
replies for replaced projects. The menu observes the same revision as the trees. While open,
it also requests hidden projects' catalogs to distinguish plain project checkboxes from
worktree submenus, without enabling their browsing scopes. Root-token catalog requests carry
the host caller identity. Failures appear on the project row instead of leaving loading
placeholders. Saved scope choices survive retries.
