# Install

## Linux

Install the prerequisites from [Requirements](requirements.md), then run:

```sh
git clone https://github.com/droserasprout/slopworld.git
cd slopworld
RIMWORLD=/path/to/RimWorld/game make install
slopworld
```

`RIMWORLD` is the directory containing the native Linux game executable. The launcher
must be used so SlopWorld gets its own game profile.

## macOS

Use the separate [macOS installation](guides/macos.md). It installs the mod into the
native Mac game and runs the daemon in Docker.

## Sidecar worker

Use a [sidecar worker](guides/sidecar.md) when the daemon and agent sandboxes should
run in Docker independently of the game host.
