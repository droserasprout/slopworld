# macOS

>macOS support is experimental and was only tested in VM. Keep your expectations low!

You can run SlopWorld on Apple silicon in a hybrid mode. RimWorld and the mod run natively, while the `slopd` daemon and agent sandboxes run
in a Linux sidecar container. See [Sidecar mode](sidecar.md) for standalone use.

## Before you start

You need a RimWorld **1.6** app and Homebrew.

## One-time setup

Clone the repository and install `just`:

```sh
git clone https://github.com/droserasprout/slopworld.git
cd slopworld
brew install just
just --justfile mac/justfile setup
open -a Docker
```

`setup` installs build tools, the .NET SDK, and Docker Desktop. Open Docker
Desktop before continuing.

## Install and launch

```sh
just --justfile mac/justfile all
```

This builds and checks the sidecar image, installs the mod, starts the configured
container, and launches native RimWorld.

The default `MAC_RIMWORLD` is `~/Documents/RimWorld.app`. Override it for a game
installed elsewhere:

```sh
MAC_RIMWORLD=/path/to/RimWorld.app just --justfile mac/justfile all
```

For a built mod, you can also run the installer from the checkout:

```sh
slopworld mod install --source mod
```

On macOS, the destination defaults to `~/Documents/RimWorld.app/Mods`, use `--game` or `MAC_RIMWORLD` to override.

Remove the mod with `slopworld mod uninstall`; it uses the same game path defaults
and accepts `--game` for another app bundle.

The game uses a separate SlopWorld profile. See [Game profiles](../maintenance/game-profiles.md)
for profile behavior and [Paths and files](../reference/paths.md) for locations.

To update, pull the latest source and run `just --justfile mac/justfile all` again.
This rebuilds the sidecar, reinstalls the mod, and launches the game. Rebuilding
the image preserves configured data and endpoint directories. Replacing the
container ends running agents and tmux sessions; see [Sidecar lifecycle](sidecar.md#lifecycle).
