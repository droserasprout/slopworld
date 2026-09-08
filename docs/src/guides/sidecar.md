# Sidecar worker

A sidecar runs `slopd`, tmux, and the agent sandboxes in a Docker container. The game
can remain on the host and connect to the sidecar over a loopback port. A sidecar is an
execution mode; it is separate from the native macOS installation described in the
[macOS guide](macos.md).

## Requirements

- Docker or Docker Desktop
- A SlopWorld checkout

## Start a worker

Build and check the image:

```sh
./slopcar/slopcar build
./slopcar/slopcar doctor
```

Start the worker with each project workspace that agents may access. Use absolute paths;
the same path must exist in the container because paths are sent between the game and
the daemon.

```sh
SLOPCAR_CONFIG_DIR="$HOME/.config/slopworld-car" \
SLOPCAR_DATA_DIR="$HOME/.local/share/slopworld-car" \
  ./slopcar/slopcar start --workspace "/absolute/path/to/project"
```

The endpoint descriptor is written to
`~/.config/slopworld-car/endpoint.toml`. The worker publishes only its configured
loopback port (7718 by default). Add more `--workspace` options when needed.

Add credentials to the initial `start` command with
`--credential-ro SOURCE=TARGET` or `--credential-rw SOURCE=TARGET`.
For Codex, mount `$HOME/.codex/auth.json` at `/home/slop/.codex/auth.json` read-only.
Claude's rotating `$HOME/.claude/.credentials.json` needs a read-write mount at
`/home/slop/.claude/.credentials.json`.

The launcher rejects the filesystem root, whole home, Docker socket/configuration,
and paths overlapping SlopWorld's token or private state.

## Use it with the Linux game

Build the Linux launcher, then point it at the worker's endpoint and profile:

```sh
RIMWORLD=/path/to/RimWorld/game \
SLOPCAR_CONFIG="$HOME/.config/slopworld-car" make sidecar-run
```

The sidecar profile is separate from the native profile; `SLOPCAR_PROFILE` overrides
it. Start the worker before running the game. Other clients can set `SLOPD_ENDPOINT`
to the sidecar's `endpoint.toml`.

## Lifecycle

```sh
./slopcar/slopcar status
./slopcar/slopcar logs
./slopcar/slopcar stop
./slopcar/slopcar restart
```

`start` reuses an existing stopped container. After rebuilding the image or changing
mounts or resource budgets, remove the container with `rm` and repeat the original
`start` command. Configured data and endpoint directories remain in place.

The default port 7718 allows a native daemon on 7717 to run alongside the sidecar.
`--port` (or `SLOPCAR_PORT`) sets both the host publish and daemon bind when config
is first seeded. A different port requires a fresh config directory; reusing one
configured for another port is refused.

For container security flags and nested-namespace constraints, see the
[technical README](https://github.com/droserasprout/slopworld/blob/main/slopcar/README.md#outer-isolation).
