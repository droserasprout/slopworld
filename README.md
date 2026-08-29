# SlopWorld

RimWorld with colonists replaced by coding agents running in tmux.

See the [requirements](docs/src/requirements.md), [installation](docs/src/install.md),
[macOS](docs/src/guides/macos.md), and [sidecar worker](docs/src/guides/sidecar.md) guides
for setup details.

## Linux quickstart

```sh
git clone https://github.com/droserasprout/slopworld.git
cd slopworld
RIMWORLD=/path/to/RimWorld/game make install
slopworld
```

`RIMWORLD` defaults to `~/GOG Games/RimWorld/game`. Run `make gogdl-login gogdl-install` to install a GOG copy.

Always launch with `slopworld`; launching RimWorld directly bypasses SlopWorld's isolated profile.
