# Surfaces with no caller

Routes and tables that nothing in this repo asks for. The mod and `slopctl` are
the only clients, so a handler with no caller in either is reachable by hand only.

## Grants

`GET /api/grants`, `POST /api/grants` and `DELETE /api/grants/:grantor` have no
in-repo HTTP caller. The grant subsystem is used internally: `manager/start.rs`
mints a scoped read-write credential for each worker at startup and revokes it if
startup fails. These routes are operator surfaces, not evidence of dead grant code.
See [agent-grants](agent-grants.md) and [daemon-workers](daemon-workers.md).

`GET /api/health` reports the live grant count and is called by `slopctl status` and by
`ConfigPage` when the configuration page loads or reloads.

## Query routes the socket already answers

`GET /api/usage`, `/api/audio` and `/api/jukebox` have no mod caller. The mod
takes those payloads from the WS events of the same name, in
`SessionHub.Handle`. All three remain for external tools -
[wire-protocol](protocol-wire.md) lists them as query routes.

Before adding a route for the mod, check whether the payload already rides the
socket.

## State rules

`[[state_rule]]` entries have no structured mod editor; edit them as raw text in
`ConfigWindow`. See [session state](daemon-session-state.md) for matching and idle decay.
