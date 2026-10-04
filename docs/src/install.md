# Install

## Linux

Install the prerequisites from [Requirements](requirements.md).
Then run these commands:

```sh
git clone https://github.com/droserasprout/slopworld.git
cd slopworld
RIMWORLD=/path/to/RimWorld/game just install
slopworld
```

`RIMWORLD` is the directory that contains the native Linux game executable.
After mod installation succeeds, the installer saves the game directory as the launcher
default in [game.toml](reference/paths.md#launcher-configuration).
The launcher checks `--game`, then `SLOPWORLD_GAME`, then the saved path,
then `~/RimWorld/game` and standard GOG and Steam paths.
If you move the game, run `RIMWORLD=/new/path/to/game just install-mod`.
An invalid saved path reports an error rather than selecting another installation.
Each successful mod installation into a Linux game's `Mods` directory replaces the
saved default. The launcher uses a separate [game profile](guides/game-profiles.md).

## macOS

Follow the [macOS installation guide](guides/macos.md).
This procedure installs the mod into the native Mac game and runs the daemon in Docker.

## Sidecar worker

Use a [sidecar worker](guides/sidecar.md) when the daemon and agent sandboxes should
run in Docker independently of the game host.
