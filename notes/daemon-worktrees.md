# Project worktree ownership

`worktrees/` owns durable records, checkout validation, bounded Git effects, and
uncommitted directory guards. `application/worktrees.rs` coordinates creation,
rename, removal, and recovery; `application/projects.rs` coordinates project edits
and relocation. Session adapters supply operation guards, identity allocation,
and attachment checks.

Worktree HTTP requests use host-user authority for root credentials, independent
of the terminal session header. Scoped credentials retain the grantor's project
restriction. Worker requests separately require an agent owner or the host user;
a host terminal cannot own a worker.

Session adapters retain guards in owned work after requester cancellation. Creation
holds the shared session boundary, then the worktree mutation guard; project edits,
rename, and removal take the exclusive boundary first. Configuration persistence
comes last. Never upgrade a shared session boundary while holding the worktree guard.
Worker allocation retains its checkout rollback guard through handoff under the
exclusive session boundary. Accepted project IDs require no creation-time config write.

Projects have durable IDs; sessions select a worktree by ID, with empty or `main`
selecting the original checkout. Branch names and HEAD describe state rather than
checkout identity. Task results, worker exits, one-shot cleanup, and final detachment
never commit or delete worktrees. Project removal cannot discard worktree records.

External checkout removal deletes only its record. User operations belong to
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
destination paths for interrupted recovery. Pending records require both paths
and manual inspection.
The coordinator owns the final project/reference and checkout publication
or reverse movement. Failed reverse movement must leave relocation intent, never a
ready record for an unverified path. Cache-link reconciliation uses the prepared
checkout paths during the final commit. The accepted record owner commits affected
`worktrees/<id>.toml` files with project/reference changes in the shared transaction.
