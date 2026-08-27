# Agent collaboration

## Scoped grants

A scoped grant lets one agent watch or drive another agent's terminal. The user
configures grants in the agent editor; agents cannot delegate access themselves.

| Level | Permissions |
| --- | --- |
| `ro` | List, read, and watch a session (state, screen, `capture-pane`). |
| `rw` | Everything in `ro`, plus input and lifecycle (keys, resize, start, stop, restart). |

Session creation is root-only (the mod's token). Host sessions are never in a grant's
scope; only the root token can touch them.

Grants are stored in the agent's configuration and survive restarts. The daemon
resolves tokens to capabilities at each request; five enforcement points cover REST
handlers, WebSocket subscriptions, session visibility, host-session filtering, and
root-token passthrough.

## Injection

A granted agent needs the daemon's address and token at spawn time. The daemon injects
them through environment variables or a pre-exec file. A running sandbox cannot receive
new environment variables, so the grant must be configured before the agent starts.

The agent reads `endpoint.toml` for the URL and token, or `SLOPD_URL` and `SLOPD_TOKEN`
when a scoped endpoint is injected.

## Task mailboxes

`slopctl` provides structured task delegation between agents. Every task has an opaque
id, sender, recipient, state, body, optional note, and timestamps. Both participants
can read a task; only the recipient changes its state.

Task states: `pending`, `accepted`, `in_progress`, `finished`, `failed`.

The `host` principal represents the user at the keyboard. A bare `slopctl` command
(without `SLOPWORLD_SESSION`) acts as `host`, which the daemon accepts only from the
root token. An agent in a sandbox identifies itself through `SLOPWORLD_SESSION`.

A scoped grant supplies the caller identity and must cover the recipient to delegate.
Task authority is deliberately narrower than terminal authority; the grant's session
scope serves as the delegation allowlist.

See [Using slopctl](slopctl.md) for the CLI reference.

## Current limits

Grant injection requires the daemon's HTTP listener to be reachable from the sandbox.
Agents with `network = "none"` cannot use grants or tasks. Agents with
`network = "private"` can reach the listener if the daemon binds on a routable address.
A Unix socket bound into granted sandboxes is planned but not yet available.
