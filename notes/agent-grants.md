# Scoped grants

A scoped grant lets one caller watch or drive selected non-host sessions without
exposing host sessions. Grants are bearer tokens minted in memory by root-authorized
requests or by the daemon when it starts a worker.
See [wire-protocol](wire-protocol.md) and [agent-tasks](agent-tasks.md).

## Permissions

- `ro` lists, reads, and watches sessions in the grant's scope.
- `rw` adds keys, resize, and session lifecycle operations.
- Session creation and host-session access remain root-only.
- The grant is revoked explicitly or when its grantor session disappears. Daemon restart
  also drops all grants.

REST and WebSocket authorization resolve the token to its session scope. Session views
hide out-of-scope entries, and host sessions are filtered centrally from non-root grants.
The root token retains access to every session.

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
