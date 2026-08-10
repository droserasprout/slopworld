# Scoped grants: an agent that may watch another

Argued once so it need not be argued again; parts are unbuilt. The problem it
answers: the token is all-or-nothing, so "may reach the daemon" equals "may open a
host shell", and there is no way to let one agent see another without handing it
everything. See [wire-protocol](wire-protocol.md), [config-stores](config-stores.md).

## The model

One **root token** - the mod's, in `[daemon] token` - reaches every session, host
ones included, read and write. Every other credential is a **grant**:

```
grant = token  +  which sessions  +  level (ro | rw)
```

- **ro** lists, reads and watches: state, screen, `capture-pane`. *"How's X doing?"*
- **rw** is ro plus input and lifecycle on a session it *names*: keys, resize, start,
  stop, restart. Driving an agent it was granted, not conjuring one.
- **Creating** a session - `run`, a new `session`, an errand - is **root-only** in the
  first cut. *"Start Y, give it this task"* is the user creating Y, which is in keeping
  with the user being the only one who mints. A grant is a handle on what already
  exists; making new things stays the mod's.
- **A host session is in no grant's scope, ever.** Only the root token touches a
  `Live` with `host` set. This is the whole of the old "gate the host terminal"
  problem: the daemon never asks *mod or agent*, only *does this grant cover this
  session* - and the answer for a host pane under any minted grant is no,
  structurally, even if the token leaks entire.

## Two decisions, fixed

- **Only the user grants.** No agent holds the grant capability - there is no
  delegation path, attenuated or otherwise. An orchestrator that wants a worker watched
  is the user asking for it, not the orchestrator granting it.
- **The scope is a session's own, edited where the session is edited and preserved.**
  Revised 2026-08-07, and it reverses the first cut below: what a session may watch or
  drive is a field on `SessionCfg`, set in the agent-edit dialog and written to
  `config.toml`, so it survives a restart and reads back into the dialog. The built
  enforcement does not care where a scope comes from - it resolves a token to a `Cap`
  and checks it - so this is a second, persistent *source* of grants, resolved from the
  agent's own config, alongside (or in place of) the ephemeral mint route.

  Open when we pick this up: whether the config-declared scope **replaces** the
  in-memory `/api/grants` mint or **complements** it. The transient "how's X doing right
  now" wanted the ephemeral one; a standing "this agent always watches that one" wants
  the config one. The host-never rule holds either way - a host session is not in
  `config.toml` to name.

### First cut, superseded by the above (kept for the reasoning)

- **Grants were to be ephemeral** - in memory, keyed to the grantor's life, nothing on
  `config.toml`, revocation just "the grantor is gone". Chosen for no long-lived secret
  in the config-stores seam and auto-cleanup of a walked-off agent. The persistence the
  user now wants trades that for a scope that survives a restart and is edited in one
  place; the injected token itself can still be ephemeral even when the scope is not.

## Enforcement, five choke points

`auth` resolves the token to a grant and hangs it on the request; then

1. REST `:name` handlers check the grant covers that session at the level the verb
   needs - read for `GET`, rw for start/stop/restart/run/put/delete.
2. `ws_run` carries the grant into its pump: a `Sub` wants ro on that session, a
   `Keys` or `Resize` wants rw (`api.rs`, the message loop).
3. `views()` filters the session list to scope, so an agent only *sees* what it may
   touch - a name it cannot read is a name it never learns.
4. Host sessions are filtered out of every non-root grant centrally, so no handler
   has to remember rule three of the model.
5. The root token is the grant that covers all, so the mod's path is unchanged.

## Injection, and the seam with the sandbox

A granted agent needs slopd's address and its token. Set **at spawn** - an env pair
or a file written before `exec` - because new env cannot be pushed into a running
sandbox. So *"start Y with a task"* fits: Y is fresh. *"How's X doing?"* to an
**already-running** X needs the daemon to write a file bound into X's sandbox that X
re-reads; that channel is the one piece not in the first cut. The daemon's
`endpoint.json` is now the mod's url+token handoff; granted-agent injection remains
a separate spawn-time capability channel.

Lines up with the netns work (the sandbox loses host loopback): the API then rides a
**unix socket bound only into granted sandboxes**. The socket's presence is the coarse
capability; the grant scopes within it. Until then it is loopback, which every
net-enabled sandbox already reaches.

## Order

1. **Done.** The core, in `grant.rs`: `Level`, `Grant`, `Cap`, `Grants`. `auth`
   resolves a token to a `Cap` and hangs it on the request (`main.rs`); the session
   routes and `/ws` check it (`guard`, `guard_create`, `scope_event`, the per-message
   `cap_ok` in `ws_run`); everything else is behind `require_root` - default deny, so a
   route is the mod's until moved into the scoped bucket on purpose. `POST /api/grants`
   mints (root-only, host refused), `DELETE /api/grants/:grantor` revokes, and `forget`
   revokes on exit. Root token behaves exactly as before; all tests green.
2. **Injection.** Spawn-time delivery of a grant's url+token into the granted agent's
   sandbox - an env pair or a file written before `exec` - and a mod UI to cut and
   revoke a grant without curl.
3. The bound-file channel for a grant to a session already up, and a fuller
   `GET /api/grants` (grantor, sessions, level; never the token).
