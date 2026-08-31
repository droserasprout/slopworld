# Agent task mailboxes

`slopctl` is the product-neutral delegation interface. `slopd` owns a durable
`tasks.toml` beside `config.toml`; sandboxes never share or edit that file.

```
slopctl delegate AGENT TASK...
slopctl spawn [--project PROJECT] [--template PRESET] [--durable] PARENT TASK...
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
`prune --all` (root) is what stops `tasks.toml` being append-only.

The CLI reads `endpoint.toml`, or `SLOPD_URL` and `SLOPD_TOKEN` when a caller supplies a
scoped endpoint. This works from the host and from agents whose network can reach the
configured HTTP listener; `network = "none"` cannot use the mailbox API.

The grant's session scope is the delegation allowlist. Task authority is separate from
terminal input authority in storage and routes.

`slopctl spawn` is different from delegation: only the root token may create a daemon-owned
worker session, and the daemon returns both its task and generated session identity. See
[daemon-workers](daemon-workers.md) for the task bootstrap, sidebar metadata, and exit/retry
policy. A worker still uses the ordinary exact-ID lifecycle commands shown above.
