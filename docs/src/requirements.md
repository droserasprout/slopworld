# Requirements

## Host system

- **Linux** only. Any modern distro will do.
- **systemd**
- **Bubblewrap** - for sandboxing.
- **passt (pasta)** - for networking
- **tmux** - terminal multiplexer.

## Build toolchain

- **Rust** toolchain for daemon and launcher
- **Mono** (`csc`) for building the mod (the .NET SDK is optional for formatting)

## RimWorld

Native Linux RimWorld build. Set the $RIMWORLD env var to the path to the game.

Tested with GOG; Steam should work the same way.
