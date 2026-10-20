# Session lifecycle boundary

`session/lifecycle/` owns start, stop, adoption, and reconciliation.
`session/sessions.rs` owns configured session edits. Operations validate a
complete candidate before changing tmux identity and protect commit/rollback from
caller cancellation. Creation, label edits, deletion, and private-state restoration
also retain their session boundary in owned work through publication or cleanup.

`session/launch.rs` owns transient launch requests and reader context separately from
`SessionCfg` and saved library entries. Execution source distinguishes host lifetime,
agent templates and cloned agent settings; destination distinguishes selected and
temporary projects. `application/launch.rs` coordinates explicit and saved launches
under the session operation guard; `session/errands.rs` owns live allocation. Template/source
lookup and worktree resolution remain inside that guard before allocation.
`session/role.rs` distinguishes durable and temporary agents, saved host shells, and
unsaved host actions. Stop and process exit retain durable agents and saved shells;
only temporary agents own private state that teardown deletes. Worker metadata
restores agent lifetime during adoption and takes precedence over stale host identity.
Capture and input owners remain shared across roles. Start plans snapshot the role
alongside launch inputs; transport booleans are derived only when needed.

Live rows retain this context across process restarts; start plans snapshot it before
I/O. Reader labels, scope, pin state, and position recover through tmux metadata,
never through saved agent records.

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
