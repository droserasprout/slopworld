# Requirements

## Host system

- **Linux** for the complete installer and launcher. Any modern distro will do.
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

## Experimental macOS worker

The native game and mod can connect to the Arch Linux `slopcar` worker through Docker Desktop.
Docker supplies Bubblewrap, pasta, tmux and the agent CLIs; it must be able to run `linux/amd64`
containers. Apple Silicon uses emulation because Arch's official image is currently amd64-only.
See the [sidecar instructions](../../slopcar/README.md). A native macOS installer and launcher are
not included yet.
