# Shared browse scopes

`SidebarScopes` adapts the game-free `BrowseScopeCatalog` for Files, Git, Search,
and the global filter menu. The catalog owns checkout selection mutations,
including committed identities received before descriptors arrive; the adapter
persists choices. Project-name filtering and checkout selection are independent.

An empty project filter means All; unknown saved keys match nothing. Hiding a
project from All materializes the remaining names and loose group. `[no projects]`
represents an explicit empty selection; choosing All clears it. Successful project
creation joins an explicit filter without narrowing All.

Main is selected by default. All worktrees includes future checkouts; selecting
one checkout from All narrows the selection. Persist the mode under the stable
project scope key. Hidden projects retain checkout choices. Enabling a project
with no selected checkouts selects All worktrees. Creating or adding a worktree,
including through the worker dialog, selects its returned identity and includes
its project without discarding other choices.

Failed refreshes retain choices and data; accepted removals drop obsolete tree
nodes. Empty-state reasons come from the catalog. Selections and history use
relative paths within daemon-assigned project/worktree identities; names must not
substitute for missing IDs. Project and checkout folds are independent. Return
detached snapshots so callers cannot mutate retained identity.

`ContentTreeGroup.Checkouts` shares Files/Git heading policy. A project's lone
selected main checkout uses the project fold and retains its scope key for actions
and refreshes. Multiple selected checkouts, or a lone non-main checkout, retain
separate checkout folds; hidden checkout folds must not suppress a merged root.
Checkout headings carry branch and cached Git shortstat, including a main checkout
merged into its project row. Standalone project headings omit shortstat.
Git retains the root branch or detached commit after a middle dot even when the root
shares its project heading.

`SessionStore` owns request-boundary translation; [navigation](mod-sidebar-navigation.md)
owns semantic history; [sidebar integration](mod-sidebar.md) owns row visibility
and hit testing. See `SidebarProjectStateTests` for filter persistence and inclusion.
