# Agent collaboration

Scoped grants let agents read or control selected agent sessions. Host sessions
remain accessible only with the root token.

## Grant permissions

| Level | Permissions |
| --- | --- |
| `ro` | List, read, and watch sessions, including state, screen output, and `capture-pane`. |
| `rw` | All `ro` permissions, plus sending keys, resizing, starting, stopping, and restarting sessions. |

Only the root token can create grants or create agents directly. See the
[API reference](../reference/api.md) for creating grants.
Scoped agents can spawn workers only in their own project and from templates
allowed by daemon policy. See [Worker configuration](configuring-agents.md#workers).

## Give an agent access

For a manually created grant, follow the [CLI connection instructions](slopctl.md#connection)
with the scoped token. Workers receive their connection credentials automatically;
see [Using slopctl](slopctl.md) for spawning them.
Keep root credentials private, as explained in the [Security model](../reference/security.md).

Agents and workers need network access to the daemon API to use grants and tasks.
`network = "none"` prevents this access. See
[Network configuration](configuring-agents.md#network) for other modes.

## Grant lifetime

Grants survive daemon restarts when their participants keep the same identities.
They are revoked when a grantor or target is renamed, removed, or replaced.
The daemon checks token permissions on every request. See
[Paths and files](../reference/paths.md) for grant storage.

## Task mailboxes

Agents can hand work to other agents within their grant scope and report to `host`,
the user at the keyboard. Terminal permissions and task participation are separate:
`ro` or `rw` controls terminal access. See [Using slopctl](slopctl.md) for task
commands, statuses, and lifecycle.

Tasks are shared by sender and recipient. Only the recipient can update an
uncanceled task. The root token can cancel queued or accepted tasks through the
task board or API; `slopctl task` has no cancel subcommand. Participants can remove
terminal tasks; root can also remove unfinished tasks. Only root can send as `host`.

Bulk task cancellation and removal validate the entire selection before changing
anything. Disk failures can then leave partial completion: replies identify committed,
unchanged or absent, failed, and unattempted IDs. The task board applies completed
changes and refreshes even when it reports a failure. Retrying a batch is safe; an
already canceled task stays canceled, and an already removed task stays absent.
Requests already handed to storage may finish after a client disconnects.
