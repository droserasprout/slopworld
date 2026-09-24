# Scoped grants

A scoped grant lets one caller watch or control selected non-host sessions without
exposing host sessions. Grants are bearer tokens.
The daemon creates them for root-authorized requests or when it starts a worker.
It saves them in the private `grants.toml` store.
See [wire-protocol](protocol-wire.md) and [agent-tasks](agent-tasks.md).

## Permissions

- `ro` lists, reads, and watches sessions in the grant's scope.
- `rw` adds keys, resize, and session lifecycle operations.
- The root token alone can create agents directly, replace the full configuration, or access host sessions.
- Scoped callers can create workers only from templates that the daemon allows.
- Removal, rename, or identity replacement revokes every grant owned by or targeting that
  session, including access to its other targets. Callers need a fresh grant afterward.
  Stop/start preserves ordinary session grants. Daemon restart restores grants only when the
  same grantor and target identities are still present.
  The daemon removes stale grants at startup.

REST and WebSocket authorization resolve the token to its session scope. Session views
hide out-of-scope entries. The daemon filters host sessions from non-root grants centrally.
The root token retains access to every session.

Each grant has an immutable scope and a shared revocation flag. Removing a grant sets the flag
and revokes already-resolved capabilities. Event filtering and socket sends check that flag
without acquiring the session boundary.
Revocation also closes connected sockets.

`manager/boundary.rs` serializes authorization/use with lifecycle changes and ephemeral cleanup.
REST owns this boundary in the router, after bounded body collection and before authorization.
Nested lifecycle calls share it and delay disk reloads until the request completes.
New asynchronous session work acquires it again. Queued terminal input checks session and process identity before
each send.

## API and delivery

`POST /api/grants` is root-only and accepts a grantor, session list, and level. It returns
the bearer token once. `GET /api/grants` returns the active count, and
`DELETE /api/grants/:grantor` revokes grants for a grantor.

Workers receive a new scoped credential through `SLOPD_URL` and `SLOPD_TOKEN` at startup.
See [daemon-workers](daemon-workers.md). For manually created grants, callers arrange delivery
of the daemon URL and token. Private networking forwards only the daemon TCP port
from the namespace when the listener uses `127.0.0.1` or all IPv4 interfaces;
it does not expose other host loopback services. A routable listener needs no
forward. The sandbox must still be able to reach the listener.
Sessions with `network = "none"` therefore cannot use grants.
No Unix-socket transport is available.
`endpoint.toml` remains the root mod and `slopctl` URL-token handoff, not grant injection.

Task mailboxes use the grant's session scope as their delegation allowlist, but task state
is separate from terminal input.
