# Updating and uninstalling

## Updating

Pull the latest source and reinstall:

```sh
cd slopworld
git pull
RIMWORLD=/path/to/RimWorld/game make install
```

`make install` rebuilds the daemon, runner, and mod, installs them, and restarts the
systemd service only when the daemon binary changed. Tmux sessions and the game survive
a daemon restart; the daemon rebuilds each running agent's terminal emulator from the
surviving tmux pane.

Snapshot versions follow the date of the tag at `HEAD`. An untagged checkout appends
the short hash.

## What is preserved

| State | Survives update |
| --- | --- |
| `config.toml` (agents, projects, settings) | Yes |
| Per-agent private state (`sessions/<state-id>/`) | Yes |
| Game profile (saves, mod settings) | Yes |
| User presets (`presets/*.toml`) | Yes |
| Jukebox stations and likes | Yes |
| Task mailbox (`tasks.toml`) | Yes |
| Prompt summaries | Yes |
| Daemon token | Yes (regenerated only on first run or manual delete) |

Unknown config fields are dropped on the next write. SlopWorld is still before its first stable
release; wire formats, config names, and path layouts may change between versions without
migration.

## Compatibility

Old daemon switches and removed config fields are rejected, not silently converted.
If the daemon fails to start after an update, check its log for parse errors:

```sh
journalctl --user -u slopd -n 30 --no-pager
```

Remove unrecognized fields from `~/.config/slopworld/config.toml` to fix the parse.

## macOS

Pull changes and rebuild the Mac installation:

```sh
gmake mac
```

This rebuilds the Docker worker, reinstalls the native mod, and launches the game.

## Sidecar worker

See [Sidecar worker](guides/sidecar.md) for the image and container lifecycle. Rebuilding
the image does not remove the configured data or endpoint directories.

## Uninstalling

```sh
make uninstall
```

This removes the daemon binary, systemd unit, runner, and the mod from the game's Mods
folder. Configuration and profile data are left alone.

To remove configuration and data:

```sh
rm -rf ~/.config/slopworld
rm -rf ~/.local/share/slopworld
```

The game profile defaults to `~/.local/share/slopworld/profile`. Back up saves before
deleting it.
