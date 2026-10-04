> “Portions of the materials used to create this content/mod are trademarks and/or copyrighted works of Ludeon Studios Inc. All rights reserved by Ludeon. This content/mod is not official and is not endorsed by Ludeon.”

# SlopWorld

SlopWorld replaces RimWorld colonists with coding agents. Each agent operates in a tmux session.

See the [requirements](docs/src/requirements.md), [installation](docs/src/install.md),
[macOS](docs/src/guides/macos.md), and [sidecar worker](docs/src/guides/sidecar.md) guides
for setup instructions.

## Linux installation

```sh
git clone https://github.com/droserasprout/slopworld.git
cd slopworld
RIMWORLD=/path/to/RimWorld/game just install
slopworld
```

`RIMWORLD` defaults to `~/GOG Games/RimWorld/game`. Run `just gogdl-login gogdl-install` to install a GOG copy.

Always start the game with `slopworld`. Mod will refuse to run on vanilla game profile.
