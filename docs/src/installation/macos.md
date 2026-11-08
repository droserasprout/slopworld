# macOS

> [!WARNING]
> macOS support is experimental and has not been tested with RimWorld 1.7 (Waylith).

You can run SlopWorld on Apple silicon in a hybrid mode. RimWorld and the mod run natively, while the `slopd` daemon and agent sandboxes run
in a Linux sidecar container. See [Sidecar mode](sidecar.md) for standalone use.

## Before you start

You need a RimWorld **1.7** app and Homebrew.

## One-time setup

Clone the repository and install `just`:

```sh
git clone https://github.com/droserasprout/slopworld.git
cd slopworld
brew install just
just mac-setup
open -a Docker
```

`setup` installs build tools including Rust, the .NET SDK, and Docker Desktop. Open Docker
Desktop before continuing.

## Prepare the sidecar image

Build the base runtime explicitly:

```sh
just sidecar-image
```

Extend it with your Linux agent CLIs as described in [Sidecar mode](sidecar.md),
then export `SLOPCAR_IMAGE` with your custom image name. Installation and launch
use that image without rebuilding it.

## Install and launch

```sh
just mac-dev
```

This checks the selected sidecar image, installs the mod, starts the configured
container, and launches native RimWorld.

The default `MAC_RIMWORLD` is `~/Documents/RimWorld.app`. `MAC_GAME` selects the native executable and
`MAC_MANAGED` selects the directory containing the game assemblies. Both default
to the bundle's `Contents/MacOS` layout. Select a bundle installed elsewhere with:

```sh
MAC_RIMWORLD=/path/to/RimWorld.app just mac-dev
```

To reinstall without launching the game, run from the repository root:

```sh
just mac-install
```

This builds and installs the mod into `MAC_MODS`, which defaults to
`~/Documents/RimWorld.app/Mods`. Reuse your `MAC_RIMWORLD` override for another bundle.

The macOS recipes build the launcher in the checkout without installing it into
`PATH`. Remove the mod from the repository root with:

```sh
slopd/target/debug/slopworld mod uninstall
```

Use `slopd/target/release/slopworld` if you built with `BUILD=release`.
The command defaults to `~/Documents/RimWorld.app/Mods`; pass
`--game /path/to/RimWorld.app` or reuse `MAC_RIMWORLD` for another bundle, or
pass `--mods /path/to/Mods` if you overrode `MAC_MODS`.

The game uses a separate SlopWorld profile. See [Game profiles](../maintenance/game-profiles.md)
for profile behavior and [Paths and files](../reference/paths.md) for locations.

To update, pull the latest source and run `just mac-dev` again.
This reinstalls the mod and launches the game. To update the sidecar, explicitly
rebuild the base with `SLOPCAR_IMAGE=slopcar:local just sidecar-image`,
then rebuild your custom image. Image builds preserve configured data and endpoint directories. Replacing the
container ends running agents and tmux sessions; see [Sidecar lifecycle](sidecar.md#lifecycle).
