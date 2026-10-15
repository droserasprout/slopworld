# Sidecar mode

A sidecar runs `slopd`, tmux, and the agent sandboxes in a Docker container. The game
can remain on the host and connect to the sidecar over a loopback port. This page covers standalone sidecar setup. The native-game
[macOS workflow](macos.md) uses the same Linux sidecar.

## Requirements

You need Docker or Docker Desktop. Building the host-side launcher and image
also requires Rust, `just`, and a local repository checkout.
The container supplies the daemon runtime dependencies. To build the launcher and
mod for the Linux game below, also install the host
[build prerequisites](../development/build.md#prerequisites) and locate your native RimWorld 1.7
installation.

## Start the sidecar {#start-a-worker}

Build and install the host-side Rust launcher, then build and check the image:

```sh
just sidecar install
just sidecar image
just sidecar doctor
```

Pass `--workspace` for each project that agents may access. Use absolute paths.
The container mounts each workspace at the same path. The game and daemon exchange workspace paths.

```sh
slopcar start --workspace "/absolute/path/to/project"
```

The installed launcher embeds its seccomp profile and runtime commands do not
require a checkout. `slopcar build --source /path/to/slopworld` builds an image
from an explicit checkout; without `--source`, it uses the current directory.
Set `SLOPCAR_IMAGE` to select an image, including one already pulled from a registry.

Before running agents, extend the standard image with your chosen Linux agent
CLIs and their dependencies, then set `SLOPCAR_IMAGE` to that custom image.
Install those tools when building the image so they survive container replacement.
The launcher supplies `PUID`/`PGID` from your Linux user, or 1000:1000 on macOS.
The entrypoint starts as root to configure the `slop` account, then drops privileges
before starting the daemon. Workspace and credential ownership stays unchanged.

The worker writes `endpoint.toml` inside its config directory.
The default host location is `<CONFIG>/sidecar/endpoint.toml`.
It publishes only its configured loopback port (7718 by default).
Add more `--workspace` options when needed.

Add credentials to the initial `start` command with
`--credential-ro SOURCE=TARGET` or `--credential-rw SOURCE=TARGET`.
For Codex, mount `$HOME/.codex/auth.json` at `/home/slop/.codex/auth.json` read-write so
refresh-token updates remain shared with the host.
Claude's rotating `$HOME/.claude/.credentials.json` needs a read-write mount at
`/home/slop/.claude/.credentials.json`.

Workspace and credential sources cannot be the filesystem root, the entire home
folder, or paths overlapping sidecar configuration or session state. Credential
mounts also protect container configuration/state targets and reject Docker socket
or configuration sources. See the [technical README](https://github.com/droserasprout/slopworld/blob/main/slopcar/README.md)
for the detailed mount constraints.

## Linux game connection

Build and install the mod into your Linux game before launching. Run these commands
from the repository root, using the same game directory for installation and launch:

```sh
RIMWORLD=/path/to/RimWorld/game just install mod
```

`install mod` builds the mod and launcher and attaches the mod to the game.
Then launch with the sidecar's endpoint and profile:

```sh
RIMWORLD=/path/to/RimWorld/game just sidecar run
```

The sidecar profile is separate from the native profile.
See [Paths and files](../reference/paths.md) for overrides and
[Game profiles](../maintenance/game-profiles.md) for profile behavior.

Start the worker before running the game. Use `slopctl --sidecar status` to connect
from the CLI. `SLOPD_ENDPOINT` can select an explicit descriptor in either mode.

## Lifecycle

```sh
slopcar status
slopcar logs
slopcar stop
slopcar restart
```

`stop`, `restart`, and container removal end all running agent and host-shell
processes, including tmux. Configuration, private agent state, and mounted project
files remain on disk. See [Session lifecycle](../agents/session-lifecycle.md) for autostart
and conversation resume.

`start` with no options reuses an existing stopped container. After you rebuild the
image or change mounts or resource limits, remove the container with
`slopcar rm`, then repeat the original `start` command, including its
directory overrides and mounts. Container removal preserves those directories.

The default port 7718 allows a native daemon on 7717 to run alongside the sidecar.
`--port` (or `SLOPCAR_PORT`) sets the host port and daemon bind port when the worker first creates its configuration.
A different port requires a new configuration directory.
The launcher rejects a configuration directory that specifies another port.

For container security flags and nested-namespace constraints, see the
[technical README](https://github.com/droserasprout/slopworld/blob/main/slopcar/README.md#outer-isolation).

Host storage uses `SLOPD_CONFIG_ROOT`, `SLOPD_DATA`, and `SLOPD_CACHE` in both
standalone and recipe workflows. Set the same environment when starting the
sidecar and launching clients. See [Paths and files](../reference/paths.md#sidecar-host-storage)
for host layout and fixed container mount targets, and
[Environment variables](../reference/environment.md#sidecar-container-controls)
for container controls. `slopcar paths endpoint` prints the resolved descriptor
path without requiring Docker.
