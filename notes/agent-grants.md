# Agent credentials and permissions

`slopd/src/credentials.rs` owns bearer identity and shared revocation state.
`session/caps.rs` authorizes actions against the caller's current `AgentPermissions`.
Root credentials retain administrative authority. Agent credentials supply self-access;
manual grants and captured relationship flags cannot authorize other targets.
Policy changes apply at the next admission or protected stream publication without
restarting the agent. Already admitted effects retain their operation guards.

Own-worker scope compares stable parent identity. Project scope compares the caller
and target's accepted project, never terminal headers. Host terminals are excluded.
Deletion excludes self. Worktree mutation checks policy and the caller's project;
external registration remains root-only. Spawn-created checkouts use the same policy.

Root HTTP mutations drain uploads before acquiring the session boundary and recheck
the captured root credential generation inside it. Token rotation invalidates pending
mutations even if a later rotation restores the same token; accepted owned work retains
the boundary through completion. Root snapshots keep their independent read path.

WebSockets retain the root credential generation for their whole lifetime. Command
admission rechecks it under the session or terminal guard, and serialized publication
rechecks it after waiting for the writer. Revocation interrupts snapshot initialization
and commands still waiting for admission; admitted effects retain their guards through
completion and cleanup.

The CLI refuses HTTP redirects so bearer headers cannot reach another endpoint.

Credentials live in `credentials.toml`, independently of the retired `grants.toml`.
No old tokens are restored. Launch, adoption, stop/exit, and identity invalidation own
credential lifetime. Credentials never enter saved agent records or tmux snapshots.
Both credential stores are excluded from sandbox mounts. Store writes remain private,
and revocation propagates to previously resolved credentials.

See [workers](daemon-workers.md), [tasks](agent-tasks.md), and
[Agent collaboration](../docs/src/agents/agent-collaboration.md).
