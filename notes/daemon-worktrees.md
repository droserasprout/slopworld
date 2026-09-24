# Project worktrees

`worktrees.rs` owns `worktrees.toml` and bounded Git operations.
`manager/worktrees.rs` handles allocation, selection, attachments, recovery, and removal.
The project keeps its original directory.
The daemon assigns its stable ID when it saves the project or creates the first worktree.
Session `worktree` is an ID, with empty or `main` selecting the original checkout.
Git reports branch names and HEAD as state. Neither identifies the worktree.

Task results, worker exits, one-shot cleanup, and final detachment never commit or delete a worktree.
Project removal and configuration updates cannot discard a project with worktree records.
Worker tmux metadata keeps project and worktree selection after its parent exits.

Each managed worktree has a dedicated container with a `checkout` directory.
For removal, mount only that container and shared Git metadata in a minimal Bubblewrap namespace.
Git can run its status helper and delete the checkout without access to the original source tree or host home.
Host Git checks block repository helpers from launching child processes.
Allocation registers the worktree without a checkout, then runs Git commands to create its branch and index.
The shared session boundary protects authorization and project identity through direct allocation
while allowing terminal input. The worktree mutation lock serializes catalog writes and Git work.
Landlock ABI 3 restricts writes to the container and shared metadata, even if an agent changes metadata symlinks.
Allocation fails if the daemon cannot enforce this restriction.
Required repository filters can also fail allocation. Keep the record and partial tree for inspection.

Removal is explicit and always refuses attached sessions and host terminals.
For an existing managed checkout, removal requires no tracked changes, untracked files, or ignored files.
Its HEAD must belong to a retained local branch.
Removal unregisters an external checkout without deleting its files.
You cannot remove `main`.
The daemon does not force deletion or commit automatically.

Interrupted operations remain visible.
Recovery recognizes completed allocation but never restarts workers or deletes files automatically.
If a checkout is missing, retry removal to clean up only its Git registration and empty container.
Sessions can attach only to ready worktrees with an existing checkout.
Interrupted and missing records remain available for inspection and explicit removal.

Resolve worktree paths before you build a sandbox or change files.
Linked worktrees use Git metadata at real paths. Check each metadata path against the registered repository.
Before selecting another worktree, edit literal mounts that expose the original checkout.
Relative mount destinations follow the selected checkout.
Cache mounts share project storage and outlive each checkout.
`sandbox/cache.rs` resolves cache sources and builds the Settings Storage inventory.
Blank sources use managed storage keyed by project ID and destination. Explicit sources remain literal paths.
Removing a worktree does not delete managed or external cache storage.
See [sandbox](sandbox-isolation.md) and [usage](../docs/src/guides/project-worktrees.md).

The daemon reads and writes only `worktrees.toml` with `worktrees` records.
Config fields are `project.worktree_root` and `session.worktree`; removed workspace names fail config loading.
Session views retain a file-stamped worktree index and reload it when the catalog changes,
including external edits. TOML parsing and serialization run on the blocking executor; atomic replacement retains
the async file helper. The existing mutation lock retains write ordering; unchanged views avoid parsing.
