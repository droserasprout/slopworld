# Session lifecycle boundary

`session/manager/lifecycle/` owns start, stop, adoption, and reconciliation.
`session/manager/sessions.rs` owns configured session edits. Operations validate a
complete candidate before changing tmux identity and protect commit/rollback from
caller cancellation.

Rename preserves durable session identity. Reconciliation cannot reinterpret a
retained host or different durable row as a configured agent with the same name;
retire the incompatible process before installing the new identity. Failed retirement
leaves that identity separate. Failed persistence restores the old tmux name; failed
rollback reports both failures and requires manual recovery.
Refreshing a saved host tab preserves its live session identity so its existing
input queue remains valid across catalog edits.

Session identity/grant changes, per-terminal input, and configuration persistence
have separate boundaries so unrelated lifecycle work need not block root terminal
input. Scoped admission remains protected through authorization. Delayed input and
attachment recheck run identity before publication.

Config acceptance belongs to [stores](daemon-config-stores.md), reader/process-exit
handling to [capture](daemon-terminal-capture.md), credentials to [grants](agent-grants.md),
and daemon-only survival to [redeploy](daemon-redeploy.md).
