# Scoped grants

A scoped grant lets one caller watch or drive selected non-host sessions without
exposing host sessions. Grants are bearer tokens minted by root-authorized requests or
by the daemon when it starts a worker, then persisted in the private `grants.toml` store.
See [wire-protocol](protocol-wire.md) and [agent-tasks](agent-tasks.md).

## Permissions

- `ro` lists, reads, and watches sessions in the grant's scope.
- `rw` adds keys, resize, and session lifecycle operations.
- Session creation, full configuration replacement, and host-session access remain root-only.
- Removal, rename, or identity replacement revokes every grant owned by or targeting that
  session, including access to its other targets. Callers need a fresh grant afterward.
  Stop/start preserves ordinary session grants. Daemon restart restores grants only when the
  same grantor and target identities are still present; stale grants are pruned at startup.

REST and WebSocket authorization resolve the token to its session scope. Session views
hide out-of-scope entries, and host sessions are filtered centrally from non-root grants.
The root token retains access to every session.

Grants have immutable scopes and a shared revocation flag, so already-resolved capabilities
lose authority when the grant is removed. Event filtering and socket sends check that flag
without acquiring the session boundary; revocation also closes connected sockets.

`manager/boundary.rs` serializes authorization/use with lifecycle changes and ephemeral cleanup.
REST owns this boundary in the router, after bounded body collection and before authorization.
Nested lifecycle calls share it and defer disk reloads until the request completes; spawned
session work reacquires it. Queued terminal input checks session and process identity before
each send.

## API and delivery

`POST /api/grants` is root-only and accepts a grantor, session list, and level. It returns
the bearer token once. `GET /api/grants` returns the active count, and
`DELETE /api/grants/:grantor` revokes grants for a grantor.

Workers receive a fresh scoped credential through `SLOPD_URL` and `SLOPD_TOKEN` at startup;
see [daemon-workers](daemon-workers.md). For manually minted grants, callers arrange delivery
of the daemon URL and token. The sandbox must reach the configured HTTP listener.
`network = "none"` therefore cannot use grants; no Unix-socket transport is available.
`endpoint.toml` remains the root mod and `slopctl` URL-token handoff, not grant injection.

Task mailboxes use the grant's session scope as their delegation allowlist, but task state
is separate from terminal input.
