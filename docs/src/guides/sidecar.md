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

Add credential options to that initial `start` command when an agent needs them. For example:

```sh
SLOPCAR_CONFIG_DIR="$HOME/.config/slopworld-car" \
SLOPCAR_DATA_DIR="$HOME/.local/share/slopworld-car" \
  ./slopcar/slopcar start \
  --workspace "/absolute/path/to/project" \
  --credential-ro "$HOME/.codex/auth.json=/home/slop/.codex/auth.json"
```

The worker does not mount the whole home directory or the Docker socket.

## Use it with the Linux game

Build the Linux launcher, then point it at the worker's endpoint and profile:

```sh
RIMWORLD=/path/to/RimWorld/game \
SLOPCAR_CONFIG="$HOME/.config/slopworld-car" make run-slopcar
```

The sidecar profile is separate from the native profile. Start the worker before running
the game.

## Lifecycle

```sh
./slopcar/slopcar status
./slopcar/slopcar logs
./slopcar/slopcar stop
./slopcar/slopcar restart
```

To rebuild after updating the checkout, run `build`, remove the container with `rm`, and
repeat the original `start` command. The configured data and endpoint directories remain
in place when the container is removed.
