# Troubleshooting

## The mod disables itself at startup {#the-mod-refuses-to-patch}

The refusal dialog means the current save-data folder lacks `slopworld.profile`.
SlopWorld disables itself in that profile's mod list and asks you to quit. Restart
RimWorld to play without it, or use the `slopworld` launcher to start SlopWorld.
If saving the mod list fails, the dialog reports that automatic disabling failed.
Launching directly, selecting the wrong profile, or losing the marker can cause it.
Use [Game profiles](../maintenance/game-profiles.md) to select and initialize the profile.

## The launcher cannot find the game

Pass `slopworld --game /path/to/game` or set `SLOPWORLD_GAME` for the launcher.
Set `RIMWORLD` for just build/install/run targets. See [Install](../getting-started/install.md).

## Harmony exceptions at startup

Search game logs for `patching incomplete:`. Use `slopctl logs game --follow`;
see [Paths and files](../reference/paths.md#logs) for log locations. These exceptions do not
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
See [Linux sidecar](../deployment/sidecar.md) or [macOS](../deployment/macos.md) for container lifecycle.

## An agent cannot reach the network

After a host network change such as a VPN toggle, private-mode connections may
hang. Restart the agent from the sidebar to recreate its private network while
preserving state. SlopWorld does not automatically restart agents to repair this.

`network = "none"` intentionally prevents connectivity. See
[Network and DNS](../sandbox/sandbox-presets.md#network) for other modes.

## An agent starts but exits immediately

Use `slopctl logs daemon` and `slopctl sandbox inspect NAME` to inspect the failed
launch. Check whether the selected command is installed and available, including
commands from removed presets. Missing preset host paths are skipped rather than
being a launch error by themselves. See [Sandbox presets](../sandbox/sandbox-presets.md).

## Multiple instances {#multiple-instances}

Launcher processes using the same profile cannot launch simultaneously on Linux
or macOS. Detection of a game launched directly is Linux-only. Use separate
[Game profiles](../maintenance/game-profiles.md) for separate instances.

## tmux says "no sessions" but agents are running

Use the SlopWorld server's socket, as described in
[Attaching to tmux](../terminals/terminal.md). Sidecar tmux runs inside its container.

## Settings changes have no effect

See [Applying changes](../customization/settings.md#applying-changes) for Save and restart requirements.

## CPU usage

See the [Security model](../sandbox/security.md) for sandbox limits and
[Requirements](../getting-started/install.md#requirements) for supported game versions.

## Configuration validation errors {#configuration-errors-after-updating}

Inspect daemon logs and correct the fields named in the error. Machine settings
belong in `config.toml`; workspace records use the locations in
[Paths and files](../reference/paths.md). Usage polling uses `[daemon.usage_items.*]` rows;
see [Usage polling](../agents/usage-and-summaries.md#usage-polling).
