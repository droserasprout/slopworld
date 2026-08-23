# SlopWorld

RimWorld with colonists replaced by coding agents running in tmux.

## Requirements

- Linux with systemd
- Native Linux RimWorld 1.6
- Rust/Cargo and Mono (`msbuild`, `csc`)
- tmux, Bubblewrap, and Passt (`pasta`)
- One supported agent CLI: Claude Code, Codex, OpenCode, or Pi

Check the full list:

```sh
RIMWORLD=/path/to/RimWorld/game make check-reqs
```

## Quickstart

```sh
git clone https://github.com/droserasprout/slopworld.git
cd slopworld
RIMWORLD=/path/to/RimWorld/game make install
slopworld
```

`RIMWORLD` defaults to `~/GOG Games/RimWorld/game`. Run `make gogdl-login gogdl-install` to install a GOG copy.

Always launch with `slopworld`; launching RimWorld directly bypasses SlopWorld's isolated profile.
