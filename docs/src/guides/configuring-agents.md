# Configuring agents

Projects provide the workspace and shared mounts. Agents choose commands and
runtime settings; templates supply initial agent settings. See
[Settings](../reference/settings.md#applying-changes) for save and restart behavior.

## Choose a project

Use **Add project** in the sidebar or command palette. The primary directory is
read-write by default; choose **Read-only** on its primary mount row to change this.
See [Configuring projects](configuring-projects.md) for extra mounts and shared caches.

## Create or edit an agent

Each agent belongs to a project and names a command preset. The command preset
identifies the software so the sandbox can mount its configuration paths.

| Field | Description |
| --- | --- |
| `command` | A command preset name. |
| `cmd` | Optional raw command line, used instead of the preset's default. |
| `args` | Extra arguments appended to the preset, daemon default, or overridden command line. Quote values containing spaces. |
| `sandbox` | Additional sandbox presets for this agent. |
| `network` | Network mode for this agent: `none`, `private`, or `host`. |
| `dns` | `resolved` to use the daemon's current resolver, or one or two explicit IPv4 server addresses. |
| `limits` | Process limits: `memory_mb`, `pids`, `nofile`, `cpu_pct`. Unset means no cap. |
| `persistent_tmp` | Keep a private `/tmp` across restarts. |
| `autostart` | Start this agent automatically when the daemon starts. |
| `auto_resume` | Resume the last conversation after a fresh process starts. |

Without `command`, `cmd` does not inherit the command preset's sandbox dependencies.

Use **Arguments** in the agent or template editor to extend the command without replacing it.
For example, `args = '--model "my model"'` adds two arguments to the preset command.
Templates copy these arguments into both agents and workers. Blank arguments leave the command unchanged.

See [Integrations and commands](../reference/integrations.md) for command apps,
agent shells, and prompt summaries. [Configuring sandboxes](configuring-sandboxes.md#network)
explains network, DNS, sandbox additions, and resource limits.

## Use a template

Open **+ > Agent** and select a template or **Custom**. Edit the copied settings as
needed. To create a template from an agent, select **Save as template**; this uses
saved settings, not unsaved changes.

Templates appear in Library. Click one to edit it, or use
**+ > Library > Agent template** to create one. Right-click to duplicate or delete.
Templates copy command and sandbox settings once. Existing agents retain their
saved settings when a template or preset changes. Templates do not store project
mounts, labels, credentials, or private-state identities.

**Session default** leaves persistent temporary storage, autostart, and auto-resume
unspecified; new agents default to `false`. Omitted network and DNS use `private`
and the daemon's current resolver. Blank limits mean no cap; set limits to positive
whole numbers. **Daemon default** uses the daemon's default command;
**Custom command** specifies a command line.

For worker templates and their allowlist, see
[Worker settings](../reference/settings.md) and [Agent collaboration](agent-collaboration.md).

## Preview and apply

The Preview tab shows settings and project mounts for the next start.
Save your changes, then restart the agent to apply command, sandbox, network,
DNS, limit, or mount changes.

**Auto-resume last conversation** applies only to a fresh process. A daemon restart
that adopts an existing terminal does not submit the resume command again.

## Private state

Restart keeps private state. Reset moves a configured agent's state to trash and
creates fresh state on its next start. Deleting a configured agent also trashes
its state. Trash is retained for at least 14 days; temporary-agent state is removed.
Configured agents keep their state while Down. See
[Backup and recovery](backup-and-recovery.md) for restoring trash entries.

## Network {#network}

Choose network and DNS in the agent editor. See
[Network and DNS](configuring-sandboxes.md#network) for modes and resolver behavior.

## Workers {#workers}

Configure template access in [Worker settings](../reference/settings.md). Workers
use the same agent settings and need daemon API access for tasks.
