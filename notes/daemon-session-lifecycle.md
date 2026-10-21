# Session lifecycle

`session/lifecycle/` owns start, stop, adoption, and reconciliation.
`application/session_edits.rs` coordinates configured edits/private-state recovery;
`session/sessions.rs` supplies live facts. Validate complete candidates before tmux
identity changes, and retain guards through commit, rollback, publication, and cleanup
after caller cancellation.

Agent deletion rejects running panes; retained dead panes do not block it. Host-tab
removal stops the shell first. Rename preserves durable identity. Failed persistence
restores the tmux name; failed rollback reports both errors and needs manual recovery.

Application resolves launch policy. `session/errands.rs` reserves identities without
publishing; the guard spans reservation through live publication. Dropping a reservation
leaves no live/temporary row. Reader context stays in `session/launch.rs`, separate from
saved definitions. `session/role.rs` owns retention: durable agents/saved shells survive
stop; temporary agents clean private state. Snapshot role/inputs before I/O.

Adoption uses tmux reader metadata rather than saved agent records. Worker metadata
wins over stale host identity. Temporary non-workers publish identity/project/worktree
metadata before launch. Invalid metadata leaves panes untouched; [workers](daemon-workers.md)
owns worker rejection. Reconciliation must not reinterpret a host or different durable
identity as a same-name configured agent. Retire incompatible processes first; failure
keeps identities separate. Host refresh preserves live identity and input queues.

Identity/grants, terminal input, and persistence have separate boundaries so unrelated
lifecycle work need not block root input. Scoped work retains authorization; delayed
input checks run identity. See [stores](daemon-config-stores.md),
[capture](daemon-terminal-capture.md), and [redeploy](daemon-redeploy.md).
