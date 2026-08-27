# Scoped grants: an agent that may watch another

Scoped grants let an agent watch or drive selected sessions without granting host
shell access. See [wire-protocol](wire-protocol.md) and [config-stores](config-stores.md).

## The model

One **root token** - the mod's, in `[daemon] token` - reaches every session, host
ones included, read and write. Every other credential is a **grant**:

```
grant = token  +  which sessions  +  level (ro | rw)
```

- **ro** lists, reads and watches: state, screen, `capture-pane`. *"How's X doing?"*
- **rw** adds input and lifecycle to named sessions: keys, resize, start, stop and restart.
- Creating sessions is **root-only**; a grant handles existing sessions.
- **Host sessions are never in a grant's scope.** Only the root token can touch `Live` values
  with `host` set.

## Two decisions, fixed

- **Only the user grants.** No agent holds the grant capability - there is no
  delegation path, attenuated or otherwise. An orchestrator that wants a worker watched
  is the user asking for it, not the orchestrator granting it.
- **A session owns its scope.** The agent-edit dialog writes it to `SessionCfg` and
  `config.toml`, so it survives restarts; enforcement still resolves tokens to `Cap` values.

  Open: decide whether config-declared scopes replace or complement the ephemeral
  `/api/grants` mint. The host-never rule applies either way.

## Enforcement, five choke points

`auth` resolves the token to a grant and attaches it to the request:

1. REST handlers require ro for reads and rw for session lifecycle or mutation verbs;
   session creation remains root-only.
2. `ws_run` requires ro for `Sub` and rw for `Keys`/`Resize`.
3. `views()` hides sessions outside the grant's scope.
4. Host sessions are filtered centrally from non-root grants.
5. The root token covers all sessions, preserving the mod's existing path.

## Injection, and the seam with the sandbox

A granted agent needs slopd's address and token at spawn time, via environment or a
pre-`exec` file; a running sandbox cannot receive new environment variables. Injection
into an already-running agent needs a daemon-written bound file. `endpoint.toml` remains
the mod's URL+token handoff.

When the sandbox loses host loopback, use a **Unix socket bound only into granted sandboxes**;
its presence is the coarse capability and the grant supplies the scope. Until then the API
uses loopback.

## Order

1. **Done.** `grant.rs` defines `Level`, `Grant`, `Cap` and `Grants`; REST and `/ws`
   enforce scope, `POST /api/grants` mints root-only grants, and deletion or exit revokes.
   Other routes remain default-deny under `require_root`.
2. **Injection.** Spawn-time delivery of a grant's url+token into the granted agent's
   sandbox - an env pair or a file written before `exec` - and a mod UI to cut and
   revoke a grant without curl.
3. The bound-file channel for a grant to a session already up, and a fuller
   `GET /api/grants` (grantor, sessions, level; never the token).

Structured delegation now lives separately in [agent-tasks](agent-tasks.md): terminal
grants authorize the first cut, but task state is not inferred from terminal input.
