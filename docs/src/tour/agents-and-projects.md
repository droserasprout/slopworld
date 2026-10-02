# Agents and projects

## Projects provide workspaces

A project supplies a working directory and shared mounts. It can also have several
checkouts. See [Configuring projects](../guides/configuring-projects.md) and
[Project worktrees](../guides/project-worktrees.md).

## Agents run commands

An agent runs a command app in a project, with its own launch settings.
See [Configuring agents](../guides/configuring-agents.md) to choose a CLI and
[Sandboxing](sandboxing.md) to inspect its access.

## Session states

- **Down** means the process is not running.
- **Working** means the terminal has recent activity.
- **Idle** means the terminal has been quiet.

A quiet terminal can still have a computing agent; state describes terminal activity.

## Related workflows

- [Library items and errands](../guides/library.md) for reusable prompts and temporary commands.
- [Host terminals](../guides/host-terminals.md) for persistent shell tabs.
- [Agent collaboration](../guides/agent-collaboration.md) for grants and task handoffs.
- [Private state](../guides/configuring-agents.md#private-state) and
  [Backup and recovery](../guides/backup-and-recovery.md) for restart, reset, and restore.
- [Prompt summaries](../reference/integrations.md#prompt-summaries) for supported CLI titles.
