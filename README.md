# SlopWorld

RimWorld with colonists replaced by coding agents running in tmux.

## Requirements

- Linux with systemd
- Native Linux RimWorld 1.6
- Rust/Cargo and Mono (`csc`)
- tmux, Bubblewrap, and Passt (`pasta`)
- One supported agent CLI: Claude Code, Codex, OpenCode, or Pi

The complete installer and game launcher are still Linux-only. On macOS, the experimental
[`slopcar`](slopcar/README.md) worker runs `slopd` and its Linux sandboxes in Docker Desktop;
RimWorld and the mod remain native.

Check the full list:

```sh
RIMWORLD=/path/to/RimWorld/game make check-reqs
```

On macOS, the friend-facing sidecar path is (using Homebrew GNU Make):

```sh
brew install make
gmake mac-setup
open -a Docker
gmake mac
```

Set `MAC_RIMWORLD` if RimWorld is not in the default Steam app-bundle path. This keeps RimWorld
and the mod native while running `slopd`, tmux, Bubblewrap, pasta and the agent CLIs in Docker.

## Quickstart

```sh
git clone https://github.com/droserasprout/slopworld.git
cd slopworld
RIMWORLD=/path/to/RimWorld/game make install
slopworld
```

`RIMWORLD` defaults to `~/GOG Games/RimWorld/game`. Run `make gogdl-login gogdl-install` to install a GOG copy.

Always launch with `slopworld`; launching RimWorld directly bypasses SlopWorld's isolated profile.
