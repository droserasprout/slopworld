# Surfaces with no caller

Routes and tables that nothing in this repo asks for. The mod and `slopctl` are
the only clients, so a handler with no caller in either is reachable by hand only.

## Grants

`GET /api/grants`, `POST /api/grants` and `DELETE /api/grants/:grantor` have no
caller. `Sessions::mint_grant` is reached only from the `api/handlers.rs` handler, so a
grant exists only after someone POSTs with the root token. The handler comment at
`api/handlers.rs` calls revocation "the mod's revoke"; that UI was never built. The
model in [agent-grants](agent-grants.md) is implemented and untriggered.

`GET /api/health` reports the live grant count and is called by `slopctl status` and by
`ConfigPage` when the configuration page loads or reloads.

## Query routes the socket already answers

`GET /api/usage`, `/api/audio` and `/api/jukebox` have no mod caller. The mod
takes those payloads from the WS events of the same name, in
`SessionHub.HandleEvent`. All three remain for external tools -
[wire-protocol](wire-protocol.md) lists them as query routes.

Before adding a route for the mod, check whether the payload already rides the
socket.

## `state_rules`

The only `config.toml` table with no mod editor. Projects, sessions, library items
and per-session limits each have one ([mod-ui-windows](mod-ui-windows.md));
`state_rules` is edited as raw text in `ConfigWindow`.
