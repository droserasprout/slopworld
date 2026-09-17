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
session disappears. The daemon checks token capabilities on each request.

## Delivery

For manually created grants, the caller must arrange delivery of the daemon URL and
scoped token through `SLOPD_URL` and `SLOPD_TOKEN`. Workers created with `slopctl spawn`
from an enabled template receive these automatically at startup. The host endpoint file contains the root token
and must not be handed to a scoped agent.

## Task mailboxes

`slopctl` provides structured task delegation between agents. Every task has an opaque
id, sender, recipient, state, body, optional note, and timestamps. Both participants
can read a task; only the recipient changes its state.

Task states: `queued`, `accepted`, `working`, `done`, `failed`, `canceled`.

A scoped grant supplies the caller identity and must cover the recipient to delegate.
Task authority is deliberately narrower than terminal authority; the grant's session
scope serves as the delegation allowlist.

See [Using slopctl](slopctl.md) for the CLI reference.

## Current limits

Using a grant requires the daemon's HTTP listener to be reachable from the sandbox.
Agents with `network = "none"` cannot use grants or tasks. Agents with
`network = "private"` can reach the listener if the daemon binds on a routable address.
No Unix-socket transport is available.
