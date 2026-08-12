# Agent task mailboxes

`slopctl` is the product-neutral delegation interface. `slopd` owns a durable
`tasks.json` beside `config.toml`; sandboxes never share or edit that file.

```
slopctl delegate AGENT TASK...
slopctl inbox [--all] [--sent] [--received] [--status STATUS]
slopctl task ID
slopctl accept|progress|finish|fail ID [NOTE...]
slopctl rm ID
slopctl prune [--all]
slopctl peers
slopctl status
```

Every task has an opaque id, sender, recipient, state, body, optional latest note
and timestamps. Both participants may read it; only the recipient changes its state.
A scoped grant supplies the caller identity and must cover the recipient to delegate.

`host` is the user at the keyboard: a principal that is not a session and never one.
`SLOPWORLD_SESSION` unset means it, which is what makes the host path a bare `slopctl`
rather than an identity the user has to invent. Only the root token may claim it, so a
session that happens to be *called* `host` may hold a grant and still not wear it.
The recipient check skips `guard` for it alone - it is in no grant's scope and names
nothing a scoped caller could learn from - which is how an agent reports back.

`inbox` shows unfinished work in both directions, newest first; the shaping is the CLI's,
not the store's, which still answers with everything the caller is party to. `--json` is
global and renders the shaped answer rather than the raw reply - a filtered task array,
a name list, a status object - so a script gets what the reader got. Removal is
shared, because the store holds one copy of a task and not one per side: a participant
drops only what has stopped moving, the root reaches a task still in flight, and
`prune --all` (root) is what stops `tasks.json` being append-only.

The CLI reads `endpoint.json`, or `SLOPD_URL` and `SLOPD_TOKEN` when a scoped endpoint
is injected. The latter is the seam for the Unix socket/private-network work described
in [agent-grants](agent-grants.md): task IPC is built, but binding a daemon socket and
credential into private sandboxes is not. Until then this works from the host and from
agents whose network can reach the configured HTTP listener.

Task authority is deliberately narrower than terminal authority in storage and routes.
The first cut reuses a grant's session scope as the delegation allowlist; a later config
field can split `task_delegate` from terminal `ro` without changing the task protocol.
