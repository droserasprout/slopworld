# Updating and uninstalling

## Updating

Get the latest source and install it:

```sh
cd slopworld
git pull
RIMWORLD=/path/to/RimWorld/game just install
```

This rebuilds and installs the daemon, runner, and mod. The native Linux service
restarts when its binary or effective service unit changes; an inactive service
is started. Tmux sessions and the game continue through a daemon restart.

Installation preserves configuration, the configured token, private state,
complete template directories, and game profiles. See [Paths and files](reference/paths.md)
for locations and [Backup and recovery](guides/backup-and-recovery.md) for backup scope.
If startup fails after an update, see [Troubleshooting](reference/troubleshooting.md).

## Other platforms

On macOS, pull the latest source and run `just mac` to rebuild the sidecar,
reinstall the mod, and launch the game. See [macOS setup](guides/macos.md).
For standalone containers, follow [Sidecar lifecycle](guides/sidecar.md#lifecycle).
Rebuilding an image preserves configured data and endpoint directories.

## Uninstalling

```sh
just uninstall
```

This removes native Linux program files, the service unit, the mod, and any legacy
per-user loading font. It preserves configuration and profile data.

Tmux and its agents remain running after uninstall. Stop agents before deleting
their state. For the default native Linux socket, end the remaining server with:

```sh
tmux -L slopworld kill-server
```

## Delete user data

Back up saves and project data first; see [Backup and recovery](guides/backup-and-recovery.md).
Complete removal includes configuration, persistent data, and caches, including
managed build caches. Consult [Paths and files](reference/paths.md) for XDG roots,
SlopWorld overrides, and sidecar/macOS locations before deleting anything.
SlopWorld's directories do not include external projects, worktrees, or credential sources.
