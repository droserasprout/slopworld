# macOS

The macOS workflow runs RimWorld and the mod natively. The daemon and agent sandboxes
run in Docker. This page covers the Mac installation; see [Sidecar worker](sidecar.md)
for the standalone worker workflow.

## Prerequisites

- Homebrew
- A native RimWorld 1.6 app

## Install

Clone the repository and run these commands from its root:

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

The game uses a separate SlopWorld profile. `mac-run` launches the native game directly;
the Linux `slopworld` launcher is not used on macOS.

## Updating

Pull changes and run:

```sh
gmake mac
```
