# Session lifecycle boundary

`session/lifecycle/` owns start, stop, adoption, and reconciliation.
`application/session_edits.rs` owns configured session edits and private-state
recovery. `session/sessions.rs` supplies live facts, label application, reader
metadata and rename handoffs. Operations validate a
complete candidate before changing tmux identity and protect commit/rollback from
caller cancellation. Creation, label edits, deletion, and private-state restoration
also retain their session boundary in owned work through publication or cleanup.
Agent deletion rejects a running tmux pane under that boundary; stop the agent
first. A retained dead pane does not block deletion. Host-tab removal still stops
the shell before removing its saved record.

`session/launch.rs` owns transient launch requests and reader context separately from
`SessionCfg` and saved library entries. Execution source distinguishes host lifetime,
agent templates and cloned agent settings; destination distinguishes selected and
temporary projects. `application/launch.rs` coordinates explicit and saved launches
under the session operation guard. `application/launch_sessions.rs` selects templates,
resolves worktrees, captures source settings and persists host records.
`session/errands.rs` reserves a name and temporary-project identity without publishing
state; application passes back a complete plan for live publication. The guard spans
reservation through publication. Dropping a reservation leaves no live or temporary row.
`session/role.rs` distinguishes durable and temporary agents, saved host shells, and
unsaved host actions. Stop and process exit retain durable agents and saved shells;
only temporary agents own private state that teardown deletes. Worker metadata
restores agent lifetime during adoption and takes precedence over stale host identity.
Non-worker temporary agents persist their configuration in tmux before launch.
Adoption restores their private-state identity and project/worktree settings so
teardown removes the original private state. Invalid recovery metadata leaves the
pane untouched rather than inventing a replacement identity.
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
