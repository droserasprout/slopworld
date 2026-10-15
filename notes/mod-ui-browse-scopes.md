# Shared browse scopes

`SidebarScopes` adapts the game-free `BrowseScopeCatalog` into shared catalog and
selection ownership for Files, Git, Search, and the global filter menu.
The catalog owns every checkout selection mutation, including committed identities
received before their descriptors arrive; the runtime adapter persists its choices.
Checkout choices are independent of the project-name filter; Main is enabled by
default. All worktrees selects every checkout in a project, including future ones;
choosing an individual checkout from All narrows to that checkout. Persist this
mode under the stable project scope key. Project submenu indicators summarize
checkout choices as none/some/all; hidden projects show none while retaining choices.
The project checkbox and label toggle project visibility independently of checkout
choices; when no checkouts are selected, clicking the parent enables All worktrees
and includes the project. Hovering the row or clicking its arrow opens the submenu. Checkout selections reopen that submenu so
multiple choices can be made without returning to the parent row.
Successful project creation joins an explicit project filter. Adding or
creating a worktree, including through the worker dialog, selects its returned
identity and includes its project. These success handoffs preserve other choices
and the empty-set All projects contract. Failed refreshes retain previous choices
and data; accepted removal drops obsolete tree nodes. Empty-state reasons come
from the catalog, not tree chrome.

Selections/history use relative paths within stable project/worktree identities so
identical paths remain distinct. Projects use daemon-assigned IDs; names never
substitute for missing IDs. Project and checkout folds are independent.
Detached snapshots prevent callers from mutating retained catalog identity.
Request-boundary translation belongs to `SessionStore`; semantic history belongs to
[navigation](mod-sidebar-navigation.md).
