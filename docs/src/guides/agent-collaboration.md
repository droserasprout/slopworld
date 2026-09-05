# Agent collaboration

## Scoped grants

A scoped grant lets one agent watch or drive another agent's terminal. The user
creates grants through the root-only `/api/grants` route; agents cannot mint or
delegate terminal access themselves.

| Level | Permissions |
| --- | --- |
| `ro` | List, read, and watch a session (state, screen, `capture-pane`). |
| `rw` | Everything in `ro`, plus input and lifecycle (keys, resize, start, stop, restart). |

Session creation is root-only (the mod's token). Host sessions are never in a grant's
scope; only the root token can touch them.

Grants live in daemon memory and are dropped when the daemon restarts or the grantor
session disappears. The daemon resolves tokens to capabilities at each request; five
enforcement points cover REST handlers, WebSocket subscriptions, session visibility,
host-session filtering, and root-token passthrough.

## Delivery

A granted agent needs the daemon's address and token. The daemon does not inject grant
credentials into sandboxes, so the caller must arrange delivery before the agent starts.

The agent reads `endpoint.toml` for the URL and token, or `SLOPD_URL` and `SLOPD_TOKEN`
when a caller supplies a scoped endpoint.

## Task mailboxes

`slopctl` provides structured task delegation between agents. Every task has an opaque
id, sender, recipient, state, body, optional note, and timestamps. Both participants
can read a task; only the recipient changes its state.

Task states: `queued`, `accepted`, `working`, `done`, `failed`.

The `host` principal represents the user at the keyboard. A bare `slopctl` command
(without `SLOPWORLD_SESSION`) acts as `host`, which the daemon accepts only from the
root token. An agent in a sandbox identifies itself through `SLOPWORLD_SESSION`.

A scoped grant supplies the caller identity and must cover the recipient to delegate.
Task authority is deliberately narrower than terminal authority; the grant's session
scope serves as the delegation allowlist.

See [Using slopctl](slopctl.md) for the CLI reference.

## Current limits

Using a grant requires the daemon's HTTP listener to be reachable from the sandbox.
Agents with `network = "none"` cannot use grants or tasks. Agents with
`network = "private"` can reach the listener if the daemon binds on a routable address.
No Unix-socket transport is available.
