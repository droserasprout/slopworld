# Agent collaboration

## Scoped grants

A scoped grant lets one agent watch or control selected agent sessions.
Only the root token can create a grant through `/api/grants`.

| Level | Permissions |
| --- | --- |
| `ro` | List, read, and watch sessions. This includes state, screen output, and `capture-pane`. |
| `rw` | All `ro` permissions. Also send keys, resize, start, stop, or restart sessions. |

Only the root token can create agents directly.
Scoped agents can create workers only from templates that the daemon allows.
The daemon excludes host sessions from grants. The root token can access host sessions.

The daemon saves grants in a private file beside its configuration.
After a restart, the daemon restores a grant only if the grantor and target sessions keep the
same identities.
The daemon revokes a grant if the grantor or a target is removed, renamed, or replaced.
The daemon checks token permissions on every request.

## Delivery

To use a manually created grant, set `SLOPD_URL` to the daemon URL and `SLOPD_TOKEN` to the
scoped token.
When you run `slopctl worker spawn`, the daemon sets both variables for the new worker.
The host endpoint file contains the root token.
Do not give it to an agent that uses a scoped token.

## Task mailboxes

`slopctl` supports tasks between agents and the host.
Every task has an opaque ID, sender, recipient, status, body, optional note, and timestamps.
Both participants can read a task.
Only the recipient can update tasks that are not `canceled`.
The root token can also cancel queued or accepted tasks.

The `host` identity represents the user at the keyboard.
Only the root token can use `host` as the task sender.
A session named `host` does not have this identity.
Agents can send task reports to `host`.

A scoped grant identifies the sender.
Its session scope defines which agents can receive tasks.
Its `ro` or `rw` level controls terminal access.

See [Using slopctl](slopctl.md) for task statuses, commands, and lifecycle.

## Current limits

The sandbox needs network access to the daemon's HTTP listener to use a grant.
Agents with `network = "none"` cannot use the grant or task APIs.
For agents with `network = "private"`, SlopWorld forwards the daemon's TCP port
into the namespace when the listener uses `127.0.0.1` or all IPv4 interfaces.
A listener bound to a routable address is reached through ordinary outbound
networking. A private agent still needs a scoped grant and `SLOPD_URL` and
`SLOPD_TOKEN` to authenticate. Workers receive these at startup.
The daemon does not support Unix-socket transport.
