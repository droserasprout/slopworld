# Troubleshooting

## The mod refuses to patch

The mod's refusal dialog means you started the game without the launcher.
`ModBootstrap` checks for a `slopworld.profile` marker written by the launcher
into the save-data folder. Start with `slopworld` (or `make run`) instead of running
`RimWorldLinux` directly.

## The launcher cannot find the game

Set `RIMWORLD` to the directory containing `RimWorldLinux` (or `--game /path`). The
default is `~/GOG Games/RimWorld/game`. For GOG installs managed by Heroic, use
`make gogdl-install` to download the native Linux build.

## Harmony exceptions at startup

Harmony failures appear only in `Player.log`, not in the terminal that launched the
game. Search for `patching incomplete:` after a crash. The log is at:

```text
~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log
```

Use `make logs` or `slopctl logs game --follow` to show new log entries.

## The daemon is running but the mod shows no connection

Check that `endpoint.toml` exists and contains the current token:

```sh
cat ~/.config/slopworld/endpoint.toml
```

The mod reads this file for the daemon URL and token. If the file is missing or stale,
restart the daemon.

## An agent cannot reach the network

Private-mode agents run through a `pasta` network namespace. After a host network
change (Wi-Fi to Ethernet, VPN toggle), established connections may hang. Restart the agent with the sidebar's restart action.
This recreates `pasta` and preserves private state.

If the agent has `network = "none"`, it has no connectivity by design. Change it to
`"private"` or `"host"` in the agent editor.

SlopWorld does not attempt automatic recovery. Deciding that an agent is idle can kill a local
command, and resuming input can duplicate a request that completed remotely while its
response was lost.

## An agent starts but exits immediately

Check the daemon log:

```sh
journalctl --user -u slopd -n 50 --no-pager
```

Common causes include:

- A missing command preset.
- A sandbox preset that specifies a missing host path.
- A `limits` value of zero.

## Multiple instances {#multiple-instances}

The launcher holds a profile-keyed file lock. The launcher refuses a second launch for the same profile. A different profile (set with `--profile` or `SLOPWORLD_PROFILE`) may run
beside the first.

## tmux says "no sessions" but agents are running

The daemon runs tmux on a private socket. Use `tmux -L slopworld list-sessions`
instead of bare `tmux`. See [Attaching from a terminal](../guides/terminal.md).

## The daemon hangs on startup

If the service is active but the mod cannot connect, check the configured listener
and the journal. For the default port:

```sh
ss -tlnp | grep 7717
journalctl --user -u slopd -n 30 --no-pager
```

An empty result means the command found no listener on that port. Check `[daemon].bind` in `config.toml`.
Before you diagnose a startup hang, check that you are inspecting the host or container that runs the daemon.

## Settings changes have no effect

Some settings require a daemon restart (the listener bind address) or an agent restart
(network mode, DNS servers, sandbox presets). See [Settings](settings.md) for when changes apply.

## Saves from an older version {#save-migration}

SlopWorld does not migrate saves and configuration that use removed defs. Start a new planet
if the mod's defs have changed since the game created the save.

## Known limitations

### No seccomp or disk quota {#no-seccomp}

The sandbox does not apply a seccomp filter, `--new-session`, or per-agent disk quota.
Resource caps (memory, PIDs, open files, CPU) are off unless configured in the project
or agent limits.

### Project directories are read-write {#rw-projects}

SlopWorld mounts project directories, including `.git`, with read-write access. Agents can
install hooks or change git configuration. Before you grant agent access, make backup copies of your repositories.

### RimWorld 1.6 only {#rimworld-16}

The mod patches tick methods and UI targets specific to RimWorld 1.6. Earlier versions
use different signatures. Harmony patch failures appear in `Player.log` as
`patching incomplete:`.

### Debug preset exposure {#debug-preset}

The `slopworld-debug` preset is an intentionally broad host escape for game
development. It mounts the game install, profile, tmux socket, `/proc`, `/sys`,
X11/Wayland devices, and several development caches read-write.

## Diagnostics

Use `slopctl status` to check daemon and session health.
Use `slopctl logs --follow` to read combined logs. See [Using slopctl](../guides/slopctl.md) for source selection and
[Attaching from a terminal](../guides/terminal.md) for tmux access.

Set `SLOPD_LOG=slopd=debug` to enable daemon debug logging.
