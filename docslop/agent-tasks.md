# Agent task mailboxes

`slopctl` is the product-neutral delegation interface. `slopd` owns a durable
`tasks.json` beside `config.toml`; sandboxes never share or edit that file.

```
slopctl delegate AGENT TASK...
slopctl inbox
slopctl task ID
slopctl accept|progress|finish|fail ID [NOTE...]
```

Every task has an opaque id, sender, recipient, state, body, optional latest note
and timestamps. Both participants may read it; only the recipient changes its state.
A scoped grant supplies the caller identity and must cover the recipient to delegate.
A root caller states its identity with `SLOPWORLD_SESSION`; this is the host/admin path.

The CLI reads `endpoint.json`, or `SLOPD_URL` and `SLOPD_TOKEN` when a scoped endpoint
is injected. The latter is the seam for the Unix socket/private-network work described
in [agent-grants](agent-grants.md): task IPC is built, but binding a daemon socket and
credential into private sandboxes is not. Until then this works from the host and from
agents whose network can reach the configured HTTP listener.

Task authority is deliberately narrower than terminal authority in storage and routes.
The first cut reuses a grant's session scope as the delegation allowlist; a later config
field can split `task_delegate` from terminal `ro` without changing the task protocol.
