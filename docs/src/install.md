# Install

## Linux

Install the prerequisites from [Requirements](requirements.md).
Then run these commands:

```sh
git clone https://github.com/droserasprout/slopworld.git
cd slopworld
RIMWORLD=/path/to/RimWorld/game make install
slopworld --game /path/to/RimWorld/game
```

`RIMWORLD` is the directory that contains the native Linux game executable.
`RIMWORLD` configures the build and installation; the launcher uses `--game` or
`SLOPWORLD_GAME` for a custom location. The launcher searches `~/RimWorld/game`, then standard GOG and Steam paths.
Standard game locations can use bare
`slopworld`. The launcher uses a separate [game profile](guides/game-profiles.md).

## macOS

Follow the [macOS installation guide](guides/macos.md).
This procedure installs the mod into the native Mac game and runs the daemon in Docker.

## Sidecar worker

Use a [sidecar worker](guides/sidecar.md) when the daemon and agent sandboxes should
run in Docker independently of the game host.
