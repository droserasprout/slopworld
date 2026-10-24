# Shared browse scopes

`BrowseScopeCatalog` owns checkout selection for Files, Git, Search, and global filters;
`SidebarScopes` adapts/persists it. Project-name filtering and checkout selection are
independent. Committed identities may arrive before descriptors.

Catalog scheduling retains the next eligible refresh deadline; completion, invalidation,
project/filter changes, and hidden-project demand must wake it. Session filtering uses
an index rebuilt with the project revision. Git polling retains enabled checkout keys
by catalog revision while checking fold state live.

All is a mode that includes future entries. Deselecting one entry materializes the
remaining selection; explicit empty selection must stay distinct from All. Unknown
saved project keys match nothing. Hidden projects retain checkout choices under stable
scope keys. New projects/worktrees join explicit filters without narrowing All or
discarding other choices. Enabling a project with no checkouts selected chooses All.

Failed refresh retains choices/data; accepted removal prunes obsolete nodes. Locations
and history use relative paths within daemon-assigned project/worktree IDs. Never
substitute names for missing identity. Return detached snapshots so callers cannot
mutate retained state.

`ContentTreeGroup.Checkouts` shares heading/fold policy. A lone selected main checkout
merges into its project heading while retaining its scope key for actions. Other
selections keep separate checkout folds. Hidden checkout folds must not suppress a
merged root. Files/Git share structure; Git alone paints cached shortstat.

`SessionStore` translates request identities; [navigation](mod-sidebar-navigation.md)
owns history; [sidebar](mod-sidebar.md) owns visibility/hit tests.
See `SidebarProjectStateTests` for persistence and inclusion.

Middle-clicking a sidebar filter button resets its own selection to All without
opening a menu. The global filter resets projects and checkouts; view filters reset
agent statuses, file visibility, Library projects, or task criteria independently.
