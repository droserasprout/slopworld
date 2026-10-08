# Session lifecycle

The game displays sessions managed by the daemon. Each running agent has its own
process and terminal; its saved configuration and private files have separate
lifetimes.

## Lifecycle actions

| Action | Running process | Saved configuration and files |
| --- | --- | --- |
| Close and reopen the game | Agents continue while their daemon host remains running. | Reopening reconnects to the workspace. |
| Restart the daemon | Existing tmux sessions survive and are adopted. | Private state and configuration remain. |
| Stop an agent | Ends its process. | The configured agent remains Down with its private state. |
| Restart an agent | Starts a fresh process with saved settings. | Reuses its private state and project files. |
| Reboot the daemon host | Ends running processes. | Saved agents can start again according to autostart settings. |
| Reset private state | Stops the configured agent. | Moves private state to trash; the next start creates fresh state. |
| Delete a configured agent | Ends its process and removes its configuration. | Moves its private state to trash. Project files remain. |

Use an agent row's context menu for start, stop, and restart actions. Save command,
sandbox, network, or mount edits before restarting; see
[Applying changes](../customization/settings.md#applying-changes).

## Autostart and conversation resume

**Autostart** starts a configured agent when the daemon starts.
**Auto-resume last conversation** is a separate option for a fresh agent process;
it does not run when the daemon adopts a terminal that is already running.
Resume compatibility depends on the CLI. See
[Configuring agents](configuring-agents.md#preview-and-restart).

## Private state and recovery

Private state is separate from the project checkout. Resetting it is useful when
you need a fresh agent environment; it does not undo edits in the project.
Configured-agent trash remains available for at least 14 days. Follow
[Backup and recovery](../maintenance/backup-and-recovery.md) to restore it.

## Other session types

- [Host shells](../terminals/host-shells.md) retain saved project tabs after Stop; Remove
  deletes the tab as well.
- [Library errands](../workspace/library.md) use temporary sessions and do not autostart or
  auto-resume. Temporary-agent private state is removed.
- [Workers](../reference/slopctl.md#workers-and-templates) remain as stopped sessions by default;
  one-shot workers are removed on exit. An unfinished worker task fails when its
  worker exits or is stopped. Worktrees have independent lifetimes.

For a container deployment, also read [Sidecar lifecycle](../deployment/sidecar.md#lifecycle).
