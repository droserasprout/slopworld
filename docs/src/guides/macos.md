# macOS

The macOS workflow runs RimWorld and the mod natively. The daemon and agent sandboxes
run in Docker. This page covers the Mac installation.
See [Sidecar worker](sidecar.md) for the standalone worker procedure.

## Prerequisites

- Homebrew
- A native RimWorld 1.6 app

## Install

Run these commands to clone the repository and install SlopWorld:

```sh
git clone https://github.com/droserasprout/slopworld.git
cd slopworld
brew install make
gmake mac-setup
open -a Docker
gmake mac
```

`mac-setup` installs the Mac build dependencies and Docker Desktop. `mac` builds the
Docker worker, installs the mod, and launches the game.

The default `MAC_RIMWORLD` is the GOG bundle at `~/Documents/RimWorld.app`. Override it
for a Steam or other install:

```sh
MAC_RIMWORLD=/path/to/RimWorld.app gmake mac
```

The game uses a separate SlopWorld profile. `mac-run` uses the Rust `slopworld` launcher.
It explicitly supplies the native app executable, working directory, Mods directory, and sidecar endpoint.
The Mac and Linux launchers thus use the same profile initialization and launch safety checks.

## Updating

Get the latest changes.
Then run:

```sh
gmake mac
```
