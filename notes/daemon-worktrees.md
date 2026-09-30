# Project worktrees

`worktrees/mod.rs` owns `worktrees.toml` and bounded Git operations.
`manager/worktrees.rs` handles allocation, selection, attachments, recovery, and removal.
`manager/projects.rs` owns project edits and checkout relocation coordination.
`manager/directories.rs` owns newly created directories until commit, removing only its
empty directories on failure. Allocation transfers ownership to its durable allocating record.
The project keeps its original directory.
The daemon assigns its stable ID when it saves the project or creates the first worktree.
Session `worktree` is an ID, with empty or `main` selecting the original checkout.
Git reports branch names and HEAD as state. Neither identifies the worktree.

Task results, worker exits, one-shot cleanup, and final detachment never commit or delete a worktree.
Project removal and configuration updates cannot discard a project with worktree records.
Worker tmux metadata keeps project and worktree selection after its parent exits.

Managed worktrees default to `<project_path>/.worktrees/<worktree-name>`. Explicit roots use
`<root>/<project-name>/<worktree-name>`. The shared path resolver also guards cache sources.
Managed checkout paths must end with their recorded worktree name; older ID-based `checkout`
directories are rejected instead of migrated.
New managed branches use the worktree name, including generated names. Validate branch names and
collisions before creating the checkout. Allocation also refuses to overwrite an existing branch
as one atomic operation.
For removal, mount the project worktree directory and shared Git metadata in a minimal Bubblewrap namespace.
Git can run its status helper and delete the checkout without access to the original source tree or host home.
Removal can see sibling checkouts in the same project directory. The Git inspection guard blocks repository helpers.
Host Git checks block repository helpers from launching child processes.
Allocation registers the worktree without a checkout, then runs Git commands to create its branch and index.
The shared session boundary protects authorization and project identity through direct allocation
while allowing terminal input. The worktree mutation lock serializes catalog writes and Git work.
Landlock ABI 3 restricts allocation writes to the checkout and shared metadata, even if an agent changes metadata symlinks.
Allocation fails if the daemon cannot enforce this restriction.
Required repository filters can also fail allocation. Keep the record and partial tree for inspection.

Removal is explicit and always refuses attached sessions and host terminals.
For an existing managed checkout, removal requires no tracked changes, untracked files, or ignored files.
Its HEAD must belong to a retained local branch.
Removal unregisters an external checkout without deleting its files.
You cannot remove `main`.
The daemon does not force deletion or commit automatically.
When a worker request allocates a new worktree, the request owns it until startup succeeds.
Failed or canceled startup attempts remove unattached new worktrees through the normal
protected removal path. A failed durable session or a checkout that fails removal checks
stays registered for inspection; existing worktrees are never rolled back.

Interrupted operations remain visible.
Recovery recognizes completed allocation but never restarts workers or deletes files automatically.
If a checkout is missing, retry removal to clean up only its Git registration and empty container.
Worktree renames move managed checkouts beside their original path and repair Git's linked-worktree
pointers without renaming branches. Project renames leave local `.worktrees` checkouts in place.
Named directories under custom roots follow the project name. Moves refuse attached sessions.
Sessions can attach only to ready worktrees with an existing checkout.
Interrupted and missing records remain available for inspection and explicit removal.

Resolve worktree paths before you build a sandbox or change files. Sidebar run and file-action
requests carry the selected worktree ID. Empty selects Main. Reject a path belonging to another
registered checkout, even a checkout nested under Main.
Project action validation resolves symlinks and existing ancestors before accepting a path.
Linked worktrees use Git metadata at real paths. Check each metadata path against the registered repository.
Metadata mount planning fails when a present `commondir` cannot be read; only an absent file is optional.
Before selecting another worktree, edit literal mounts that expose the original checkout.
Relative mount destinations follow the selected checkout.
Relative cache mounts install checkout links to project storage that outlives each checkout.
Worktree creation reconciles links; managed removal temporarily removes only unchanged links
before Git cleanliness inspection and restores them on failure. A project rename leaves the
stable cache source unchanged.
Configuration reconciliation removes unchanged links for deleted cache rows, then installs
current links. A changed link or occupied destination requires manual recovery.
`sandbox/cache.rs` resolves cache sources and builds the Settings Storage inventory.
Blank sources use managed storage keyed by project ID and destination. Explicit sources remain literal paths.
Legacy managed cache directories move into the flat destination-key layout when links are reconciled.
Removing a worktree does not delete managed or external cache storage.
See [sandbox](sandbox-isolation.md) and [usage](../docs/src/guides/project-worktrees.md).

The daemon reads and writes only `worktrees.toml` with `worktrees` records.
Config fields are `project.worktree_root` and `session.worktree`. Removed workspace names fail config loading.
Session views retain a file-stamped worktree index and reload it when the catalog changes,
including external edits. TOML parsing and serialization run on the blocking executor.
The async file helper still replaces files atomically. The mutation lock preserves write order.
Unchanged views do not parse the catalog again.

`WorktreeState` in `session/manager/worktrees.rs` groups the mutation gate and cached disk records.
The locks remain separate so reading cached views does not acquire the mutation gate.

`worktrees/relocation.rs` owns batch move intent and rollback. Before moving files it
persists a `relocating` record with both paths. Interrupted relocation requires manual
Git and catalog repair before attachment or removal. Rename operations retain their
session and catalog guards in a detached owner through commit or rollback, even when
the requesting client disconnects.
