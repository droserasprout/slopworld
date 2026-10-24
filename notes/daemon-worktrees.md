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

Root catalog requests use host-user authority regardless of terminal headers. Base
previews use worker caller identity: a blank revision resolves the selected caller's
HEAD, as worker spawn does. Scoped callers retain project restrictions; host terminals
cannot own [workers](daemon-workers.md).
Project/worktree IDs are durable; branch and HEAD are state, not identity. Empty or
`main` selects the original checkout. Task completion, exit, and detachment never
commit or delete checkouts. Project removal cannot discard worktree records. External
registration captures that checkout's HEAD; removal deletes only its record.

Primary checkouts require a real `.git` directory or no Git metadata. Linked
checkouts must be registered as worktrees. Resolution carries the recorded repository
authority into launch; sandbox construction rechecks both gitdir and commondir before
granting writable metadata mounts. Checkout files never establish that authority.

Host-mode relative caches link to stable project storage; sidecars reject these links.
Launch verifies the link and reachable absolute source. Explicit sources do not move
with checkouts. Interrupted allocation accepts only verified cache links as untracked
changes and checks Git root/common directory. Conflicting paths need recovery, never
replacement. Checkout deletion leaves cache data intact. `sandbox/cache.rs` reports
new links for rollback; `config/transactions/cache.rs` reconciles them.

Removal checks cleanliness and retained branches with helper execution blocked, then
removes only the selected checkout's files in the daemon. Git registration cleanup
has writable authority only for the recorded metadata, never the checkout parent or
sibling checkouts. Locked worktrees and submodules require manual resolution. A
failure after file deletion retains the record for missing-checkout recovery.

Persist relocation intent before Git effects. Publish project/reference and checkout
changes together or reverse movement. Failed reversal retains pending intent for
manual inspection, never a ready record for an unverified path. Final cache
reconciliation uses prepared checkout paths. User procedures belong to
[Project worktrees](../docs/src/workspace/project-worktrees.md).

Host Git mutations require Landlock ABI v3. Runtime capabilities probe the same
policy in a disposable child once per daemon lifetime; the mod warns when unavailable.
The probe never restricts daemon threads, and mutation enforcement remains mandatory.
