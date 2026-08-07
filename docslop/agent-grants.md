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

- **Only the user mints**, from the mod. No agent holds the mint capability - there
  is no delegation path, attenuated or otherwise. An orchestrator that wants a worker
  watched is the user asking for it, not the orchestrator granting it.
- **Grants are ephemeral.** In memory, keyed to the grantor session's life; gone when
  it exits and gone on a daemon restart. Nothing lands in `config.toml`, so the
  config-stores seam grows no long-lived secret, and a walked-off agent leaves no live
  credential. Revocation is just "the grantor is gone".

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
re-reads; that channel is the one piece not in the first cut. Absorbs the
[config-stores](config-stores.md) `endpoint.json` idea - the mod's url+token handoff
is the root grant's version of the same injection.

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
   revoke a grant without curl. Absorbs `endpoint.json` for the mod's own handoff.
3. The bound-file channel for a grant to a session already up, and a fuller
   `GET /api/grants` (grantor, sessions, level; never the token).
