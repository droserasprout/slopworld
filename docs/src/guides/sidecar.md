# Sidecar worker

A sidecar runs `slopd`, tmux, and the agent sandboxes in a Docker container. The game
can remain on the host and connect to the sidecar over a loopback port. A sidecar is an execution mode.
It is separate from the native macOS installation described in the [macOS guide](macos.md).

## Requirements

- Docker or Docker Desktop
- A local copy of the SlopWorld repository

## Start a worker

Build and check the image:

```sh
./slopcar/slopcar build
./slopcar/slopcar doctor
```

Pass `--workspace` for each project that agents may access. Use absolute paths.
The container mounts each workspace at the same path. The game and daemon exchange workspace paths.

```sh
SLOPCAR_CONFIG_DIR="$HOME/.config/slopworld-car" \
SLOPCAR_DATA_DIR="$HOME/.local/share/slopworld-car" \
  ./slopcar/slopcar start --workspace "/absolute/path/to/project"
```

The worker writes `endpoint.toml` inside its config directory.
In the example above, that is `~/.config/slopworld-car/endpoint.toml`.
It publishes only its configured loopback port (7718 by default).
Add more `--workspace` options when needed.

Add credentials to the initial `start` command with
`--credential-ro SOURCE=TARGET` or `--credential-rw SOURCE=TARGET`.
For Codex, mount `$HOME/.codex/auth.json` at `/home/slop/.codex/auth.json` read-write so
refresh-token updates remain shared with the host.
Claude's rotating `$HOME/.claude/.credentials.json` needs a read-write mount at
`/home/slop/.claude/.credentials.json`.

The launcher rejects these paths:

- The filesystem root.
- The entire home directory.
- The Docker socket or configuration.
- Paths that overlap SlopWorld's token or private state.

## Use it with the Linux game

Use this command to build the Linux launcher with the worker's endpoint and profile:

```sh
RIMWORLD=/path/to/RimWorld/game \
SLOPCAR_CONFIG="$HOME/.config/slopworld-car" make sidecar-run
```

The sidecar profile is separate from the native profile.
`SLOPCAR_PROFILE` overrides the sidecar profile. Start the worker before running the game. Other clients can set `SLOPD_ENDPOINT`
to the sidecar's `endpoint.toml`.

## Lifecycle

```sh
./slopcar/slopcar status
./slopcar/slopcar logs
./slopcar/slopcar stop
./slopcar/slopcar restart
```

`start` reuses an existing stopped container. After you rebuild the image or change mounts or resource limits, remove the container with `rm`.
Repeat the original `start` command. Configured data and endpoint directories remain in place.

The default port 7718 allows a native daemon on 7717 to run alongside the sidecar.
`--port` (or `SLOPCAR_PORT`) sets the host port and daemon bind port when the worker first creates its configuration.
A different port requires a new configuration directory.
The launcher rejects a configuration directory that specifies another port.

For container security flags and nested-namespace constraints, see the
[technical README](https://github.com/droserasprout/slopworld/blob/main/slopcar/README.md#outer-isolation).
