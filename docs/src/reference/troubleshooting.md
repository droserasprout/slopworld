# Troubleshooting

## The mod refuses to patch

The refusal dialog means the current save-data folder lacks `slopworld.profile`.
Launching directly, selecting the wrong profile, or losing the marker can cause it.
Use [Game profiles](../guides/game-profiles.md) to select and initialize the profile.

## The launcher cannot find the game

Pass `slopworld --game /path/to/game` or set `SLOPWORLD_GAME` for the launcher.
Set `RIMWORLD` for just build/install/run targets. See [Install](../install.md).

## Harmony exceptions at startup

Search game logs for `patching incomplete:`. Use `slopctl logs game --follow`;
see [Paths and files](paths.md#logs) for log locations. These exceptions do not
appear in the terminal that launched the game.

## The daemon is running but the mod shows no connection

Run `slopctl status` and inspect daemon logs with `slopctl logs daemon`.
Check the host or container that actually runs the daemon. On native Linux:

```sh
ss -tlnp | rg 7717
journalctl --user -u slopd -n 30 --no-pager
```

If no listener appears, check `[daemon].bind` in the configuration and logs for
startup failures. Service active status alone does not mean the listener is ready.
If the endpoint descriptor is missing or stale, restarting the daemon recreates it.
See [Sidecar worker](../guides/sidecar.md) or [macOS](../guides/macos.md) for container lifecycle.

## An agent cannot reach the network

After a host network change such as a VPN toggle, private-mode connections may
hang. Restart the agent from the sidebar to recreate its private network while
preserving state. SlopWorld does not automatically restart agents to repair this.

`network = "none"` intentionally prevents connectivity. See
[Network and DNS](../guides/configuring-sandboxes.md#network) for other modes.

## An agent starts but exits immediately

Use `slopctl logs daemon` and `slopctl sandbox inspect NAME` to inspect the failed
launch. Check whether the selected command is installed and available, including
commands from removed presets. Missing preset host paths are skipped rather than
being a launch error by themselves. See [Configuring sandboxes](../guides/configuring-sandboxes.md).

## Multiple instances {#multiple-instances}

Launcher processes using the same profile cannot launch simultaneously on Linux
or macOS. Detection of a game launched directly is Linux-only. Use separate
[Game profiles](../guides/game-profiles.md) for separate instances.

## tmux says "no sessions" but agents are running

Use the SlopWorld server's socket, as described in
[Attaching from a terminal](../guides/terminal.md). Sidecar tmux runs inside its container.

## Settings changes have no effect

See [Applying changes](settings.md#applying-changes) for Save and restart requirements.

## CPU usage

Use [Performance diagnostics](../guides/performance-diagnostics.md) for Linux CPU
measurement, or [Terminal latency measurements](../guides/terminal-latency.md) for
input latency. See the [Security model](security.md) for sandbox limits and
[Requirements](../requirements.md) for supported game versions.

## Configuration errors after updating

Inspect daemon logs and correct the fields named in the error. Removed
`[daemon.usage]`, `[daemon.openrouter]`, and `[daemon.openai]` settings require a
manual move to `[daemon.usage_items.*]` rows; they are not converted automatically.
See [Usage polling](integrations.md#usage-polling).
