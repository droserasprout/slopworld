# macOS

RimWorld and the mod run natively on macOS. The daemon and agent sandboxes run
in a Linux sidecar container. See [Sidecar worker](sidecar.md) for standalone use.

## Before you start

See [Requirements](../requirements.md) for the game and host prerequisites.

## One-time setup

Clone the repository and install `just`:

```sh
git clone https://github.com/droserasprout/slopworld.git
cd slopworld
brew install just
just mac-setup
open -a Docker
```

`mac-setup` installs build tools, the .NET SDK, and Docker Desktop. Open Docker
Desktop before continuing.

## Install and launch

```sh
just mac
```

This builds and checks the sidecar image, installs the mod, starts the configured
container, and launches native RimWorld.

The default `MAC_RIMWORLD` is `~/Documents/RimWorld.app`. Override it for a game
installed elsewhere:

```sh
MAC_RIMWORLD=/path/to/RimWorld.app just mac
```

The game uses a separate SlopWorld profile. See [Game profiles](game-profiles.md)
for profile behavior and [Paths and files](../reference/paths.md) for locations.

For later changes, follow [Updating and uninstalling](../updating.md).
