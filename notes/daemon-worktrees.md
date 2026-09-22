# Project worktrees

`worktrees.rs` owns the independent `worktrees.toml` store and bounded Git helpers;
`manager/worktrees.rs` owns allocation, selection, attachments, recovery and explicit teardown.
The project keeps its original directory; its stable ID is allocated when saved or on first
worktree creation.
Session `worktree` is an ID, with empty or `main` selecting the original checkout. Branch names
and HEAD are observed Git state, never worktree identity.

Task outcomes, worker exits, one-shot cleanup and last detachment never commit or delete a
worktree. Project removal/config reconciliation cannot discard a project with worktree records.
Worker tmux metadata preserves project/worktree selection independently of its parent.

Managed checkouts live in an opaque per-worktree container with a `checkout` child. This lets
removal mount only that container and shared Git metadata in a minimal Bubblewrap namespace;
Git can run its status helper and remove the checkout without exposing the original source tree
or host home. Other host Git operations keep the no-child-process inspection restriction.
Allocation uses detached/no-checkout registration, then direct Git builtins for branch/index setup.
Landlock ABI 3 confines their writes to the allocated container and shared metadata, including
when an agent changes metadata symlinks; unavailable enforcement fails allocation.
Required filters can fail allocation; retain the record and partial tree for inspection.

Removal is manual and refuses attachments, changed/untracked/ignored files and HEAD without a
retained local branch. External checkouts are only unregistered; `main` is never removable.
No forced deletion or automatic commits. Interrupted operations stay visible; recovery recognizes
completed allocation but never relaunches workers or automatically deletes files. Missing-checkout
removal retries clean up only that Git registration and its empty owned container.
Only ready worktrees with an existing checkout can be attached; interrupted and missing records
remain available for inspection and explicit removal.

Resolve worktree paths before sandbox construction and file actions. Linked worktrees mount
metadata at real paths, checked against the registered repository. Literal mounts exposing the
original checkout must be edited before selecting a different worktree; relative destinations
follow the selected checkout. Cache mounts share project-owned storage independently of checkout lifetime;
`sandbox/cache.rs` owns source resolution and the Settings Storage inventory. Blank sources
use managed storage keyed by project identity and destination; explicit sources stay literal.
Neither kind is deleted with a worktree. See [sandbox](sandbox-isolation.md) and [usage](../docs/src/guides/project-worktrees.md).

WIP `workspaces.toml` records and config field names remain readable; subsequent writes use
the worktree names. The new store takes precedence and never deletes the old file.
