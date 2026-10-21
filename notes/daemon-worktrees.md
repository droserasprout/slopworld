# Project worktrees

`worktrees/` owns records, validation, Git effects, and guards for uncommitted directories.
Application coordinates project policy, relocation, and publication; session adapters
supply identity and attachment checks. Validate project edits before Git effects and
retain identity through relocation/reference updates. See [projects](daemon-projects.md)
and [configuration](daemon-config-stores.md).

Acquire the session boundary, then `application/workspace.rs`'s worktree gate, then
persistence. Creation uses the shared boundary; project edits/removal use exclusive
access. Never upgrade while holding the gate. Retain guards through cancellation and
worker checkout rollback/handoff. Runtime reads use the accepted `WorktreeCatalog`,
so failed saves and offline edits cannot change live decisions.

Root requests use host-user authority regardless of terminal headers. Scoped callers
retain project restrictions; host terminals cannot own [workers](daemon-workers.md).
Project/worktree IDs are durable; branch and HEAD are state, not identity. Empty or
`main` selects the original checkout. Task completion, exit, and detachment never
commit or delete checkouts. Project removal cannot discard worktree records. External
registration captures that checkout's HEAD; removal deletes only its record.

Host-mode relative caches link to stable project storage; sidecars reject these links.
Launch verifies the link and reachable absolute source. Explicit sources do not move
with checkouts. Interrupted allocation accepts only verified cache links as untracked
changes and checks Git root/common directory. Conflicting paths need recovery, never
replacement. Checkout deletion leaves cache data intact. `sandbox/cache.rs` reports
new links for rollback; `config/transactions/cache.rs` reconciles them.

Persist relocation intent before Git effects. Publish project/reference and checkout
changes together or reverse movement. Failed reversal retains pending intent for
manual inspection, never a ready record for an unverified path. Final cache
reconciliation uses prepared checkout paths. User procedures belong to
[Project worktrees](../docs/src/workspace/project-worktrees.md).
