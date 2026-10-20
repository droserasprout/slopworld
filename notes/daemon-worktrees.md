# Project worktree ownership

`worktrees/` owns durable records and bounded Git operations.
`session/manager/worktrees.rs` owns lifecycle/catalog coordination;
`application/projects.rs` owns project edits and relocation coordination, with session
operation guards supplied by `session/manager/projects.rs`.
`worktrees/checkout.rs` owns checkout validation and filesystem effects;
`worktrees/directories.rs` owns uncommitted directory creation.

Worktree HTTP requests use host-user authority for root credentials, independent
of the terminal session header. Scoped credentials retain the grantor's project
restriction. Worker requests separately require an agent owner or the host user;
a host terminal cannot own a worker.

Worker allocation owns the newly created checkout and its rollback guard together,
including cancellation before the allocation returns. Its owned work inherits the
worker coordinator's exclusive session boundary and request context.
`session/manager/workers.rs` owns this allocation and rollback handoff.
Worktree creation retains its shared session boundary and worktree mutation guard
in owned work through completion or rollback. Accepted projects already have durable
IDs; worktree creation reads them without a configuration transaction. Configuration
commits require the exclusive session boundary; shared worktree operations must not
upgrade it while holding the worktree guard.
Project create/edit/remove and checkout rename/removal use the exclusive session
boundary before the worktree mutation guard; configuration persistence comes last.
Removal checks and effects remain in owned work after requester cancellation.

Projects have durable IDs; sessions select a worktree by ID, with empty or `main`
selecting the original checkout. Branch names and HEAD describe state rather than
checkout identity. Task results, worker exits, one-shot cleanup, and final detachment
never commit or delete worktrees. Project removal cannot discard worktree records.

Removing an external checkout removes only its SlopWorld record. It leaves checkout
files and Git's linked-worktree registration untouched. Operational creation,
rename, removal, and recovery rules belong to
[Project worktrees](../docs/src/workspace/project-worktrees.md).

Mount/cache boundaries belong to [sandbox isolation](sandbox-isolation.md), storage
to [configuration stores](daemon-config-stores.md), project lifecycle to
[projects](daemon-projects.md), and worker lifecycle to [workers](daemon-workers.md).

Host-mode relative cache destinations are checkout links to stable project storage
that outlives worktrees/workers. Sidecar mode rejects those relative links. Launch
requires the expected link and a source reachable at its absolute path. Explicit
sources support home/environment expansion but are not relocated with the checkout.
Interrupted allocation recovery accepts only verified cache links as untracked
changes. Checkout resolution verifies its own Git root and registered common directory.
Conflicting paths/changed links require recovery rather than replacement; removing
a checkout never deletes cache data. Config reconciliation belongs to
`config/transactions/cache.rs`; `sandbox/cache.rs` validates sources and returns
each newly created link to the coordinator for rollback.
Host inspection hardening belongs to [Git](daemon-git.md).

Relocation persists intent before Git effects, retaining structured source and
destination paths for interrupted recovery. Pending relocation records require
both paths and manual inspection. Typed lifecycle phases keep
the same saved and client-facing names; structured relocation fields stay in storage.
The coordinator owns the final project/reference and checkout publication
or reverse movement. Failed reverse movement must leave relocation intent, never a
ready record for an unverified path. Cache-link reconciliation uses the prepared
checkout paths during the final commit. The accepted record owner commits affected
`worktrees/<id>.toml` files with project/reference changes in the shared transaction.
