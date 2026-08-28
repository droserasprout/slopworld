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

The native game and mod can connect to the Debian Linux `slopcar` worker through Docker Desktop.
Docker supplies Bubblewrap, pasta, tmux and the agent CLIs. The image is multiarch, so Intel Macs
run its amd64 build and Apple Silicon runs its arm64 build without emulation.
Install Homebrew's GNU Make first with `brew install make`. Then run `gmake mac-setup`, open Docker
Desktop once, and use `gmake mac` to build the sidecar, install the mod into the configured native
macOS app bundle, and launch the game. See [macOS](guides/macos.md) for setup details.
