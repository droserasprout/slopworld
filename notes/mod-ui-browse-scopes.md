# Shared browse scopes

`SidebarScopes` adapts the game-free `BrowseScopeCatalog` into shared catalog and
selection ownership for Files, Git, Search, and the global filter menu.
Checkout choices are independent of the project-name filter; Main is enabled by
default. Failed refreshes retain previous choices and data; accepted removal drops
obsolete tree nodes. Empty-state reasons come from the catalog, not tree chrome.

Selections/history use relative paths within stable project/worktree identities so
identical paths remain distinct. Project and checkout folds are independent.
Detached snapshots prevent callers from mutating retained catalog identity.
Request-boundary translation belongs to `SessionStore`; semantic history belongs to
[navigation](mod-sidebar-navigation.md).
