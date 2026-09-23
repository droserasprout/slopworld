# Updating and uninstalling

## Updating

To get the latest source and install it, run these commands:

```sh
cd slopworld
git pull
RIMWORLD=/path/to/RimWorld/game make install
```

`make install` rebuilds and installs the daemon, runner, and mod.
It also installs the supplied UI font.
It restarts the systemd service only when the daemon binary changed.

Tmux sessions and the game continue after a daemon restart.
The daemon rebuilds each running agent's terminal emulator from the existing tmux pane.

Release versions follow the SemVer tag at `HEAD`. An untagged checkout appends the UTC build date
and short hash to the package version.

## Data that updates preserve

| State | Survives update |
| --- | --- |
| `config.toml` (agents, projects, settings) | Yes |
| Per-agent private state (`sessions/<state-id>/`) | Yes |
| Game profile (saves, mod settings) | Yes |
| User library, templates, sandboxes, and apps (`{prompts,breadcrumbs,file_actions,shell_scripts,agent_templates,sandbox_presets,app_presets}/*.toml`) | Yes |
| Jukebox stations and likes | Yes |
| Task mailbox (`tasks.toml`) | Yes |
| Prompt summaries | Yes |
| Daemon token | Yes (the daemon generates it only on the first run or after manual deletion) |

Configuration patches preserve unknown fields. Other configuration writes may remove them.
Wire formats, config names, and path layouts may change between versions without compatibility
support.

## Compatibility

If the daemon fails to start after an update, check its log for configuration errors:

```sh
journalctl --user -u slopd -n 30 --no-pager
```

Correct the fields that the error identifies.
Replace the removed `[daemon.usage]`, `[daemon.openrouter]`, and `[daemon.openai]` settings with `[daemon.usage_items.*]` rows.
See [Integrations](reference/integrations.md).

## macOS

To get changes and rebuild the Mac installation, run this command:

```sh
gmake mac
```

This rebuilds the Docker worker, reinstalls the native mod, and starts the game.

## Sidecar worker

See [Sidecar worker](guides/sidecar.md) for instructions to manage images and containers. Rebuilding
the image does not remove the configured data or endpoint directories.

## Uninstalling

```sh
make uninstall
```

This removes the daemon binary, systemd unit, runner, and supplied UI font.
It also removes the mod from the game's Mods folder.
It preserves configuration and profile data.

To remove configuration and data:

```sh
rm -rf ~/.config/slopworld
rm -rf ~/.local/share/slopworld
```

The game profile defaults to `~/.local/share/slopworld/profile`. Before you remove the profile, make a backup copy of its saves.
