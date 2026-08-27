# Troubleshooting

## The mod refuses to patch

The mod's own refusal dialog appears when the game was started without the launcher.
`SlopWorldBootstrap` checks for a `slopworld.profile` marker written by the launcher
into the save-data folder. Launch with `slopworld` (or `make run`) instead of running
`RimWorldLinux` directly.

## Harmony exceptions at startup

Harmony failures appear only in `Player.log`, not in the terminal that launched the
game. Search for `patching incomplete:` after a crash. The log is at:

```
~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log
```

Use `make logs` or `slopctl logs game` to tail it.

## The daemon is running but the mod shows no connection

Check that `endpoint.toml` exists and contains the current token:

```sh
cat ~/.config/slopworld/endpoint.toml
```

The mod reads this file for the daemon URL and token. If the file is missing or stale,
restart the daemon.

## An agent cannot reach the network

Private-mode agents run through a `pasta` network namespace. After a host network
change (Wi-Fi to Ethernet, VPN toggle), established connections may hang. Restart the
agent with the sidebar's restart action; this recreates `pasta` without losing private
state. See [Known limitations](known-limitations.md).

If the agent has `network = "none"`, it has no connectivity by design. Change it to
`"private"` or `"host"` in the agent editor.

## An agent starts but exits immediately

Check the daemon log:

```sh
journalctl --user -u slopd -n 50 --no-pager
```

Common causes: a missing command preset, a sandbox preset that names a path the host
does not have, or a `limits` value of zero (rejected at startup).

## tmux says "no sessions" but agents are running

The daemon runs tmux on its own private socket. Use `tmux -L slopworld list-sessions`
instead of bare `tmux`. See [Attaching from a terminal](../guides/terminal.md).

## The daemon hangs on startup

If `systemctl --user status slopd` shows the unit active but nothing listens on port
7717, the daemon is stuck before its TCP bind. Check the journal for the pre-bind
startup path:

```sh
ss -tlnp | grep 7717
journalctl --user -u slopd -n 30 --no-pager
```

An empty `ss` result means the bind never ran. Look for errors in
`Manager::new` or `sync_from_config` in the journal output.

## Settings changes have no effect

Some settings require a daemon restart (the listener bind address) or an agent restart
(network mode, DNS servers, sandbox presets). The Settings page notes which scope
applies. See [Settings](settings.md) for the apply-behavior table.

## `dotnet format` fails but `make mod` works

`format-mod` requires the .NET SDK. The mod compiler uses Mono `csc` directly, so
`lint-mod` and `make mod` work without the SDK. Install the .NET SDK separately if you
need formatting.

## Diagnostic commands

```sh
slopctl status                                # daemon and session summary
slopctl logs --follow                         # combined game + daemon tail
curl -s localhost:7717/api/sessions | python3 -m json.tool
tmux -L slopworld list-sessions
journalctl --user -u slopd -f
```

Set `SLOPD_LOG=slopd=debug` to enable debug logging for the daemon.
