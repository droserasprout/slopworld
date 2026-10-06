# Project worktree ownership

`worktrees/` owns durable records and bounded Git operations.
`session/manager/worktrees.rs` owns lifecycle/catalog coordination;
`session/manager/projects.rs` owns project edits and relocation.
`session/manager/directories.rs` owns uncommitted directory creation.

Projects have durable IDs; sessions select a worktree by ID, with empty or `main`
selecting the original checkout. Branch names and HEAD describe state rather than
checkout identity. Task results, worker exits, one-shot cleanup, and final detachment
never commit or delete worktrees. Project removal cannot discard worktree records.

Removing an external checkout removes only its SlopWorld record. It leaves checkout
files and Git's linked-worktree registration untouched. Operational creation,
rename, removal, and recovery rules belong to
[Project worktrees](../docs/src/guides/project-worktrees.md).

Mount/cache boundaries belong to [sandbox isolation](sandbox-isolation.md), storage
to [configuration stores](daemon-config-stores.md), project lifecycle to
[projects](daemon-projects.md), and worker lifecycle to [workers](daemon-workers.md).

Host-mode relative cache destinations are checkout links to stable project storage
that outlives worktrees/workers. Sidecar mode rejects those relative links. Launch
requires the expected link and a source reachable at its absolute path. Explicit
sources support home/environment expansion but are not relocated with the checkout.
Conflicting paths/changed links require recovery rather than replacement; removing
a checkout never deletes cache data. Config reconciliation belongs to
`session/manager/config/cache.rs`; `sandbox/cache.rs` validates sources and returns
each newly created link to the coordinator for rollback.
Host inspection hardening belongs to [Git](daemon-git.md).
