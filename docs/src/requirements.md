# Requirements

## Linux

- **Linux** for the complete installer and launcher. Any modern distro will do.
- **systemd**
- **Bubblewrap** - for sandboxing.
- **passt (pasta)** - for networking
- **tmux** - terminal multiplexer.

## RimWorld

Native Linux RimWorld build. Set the $RIMWORLD env var to the path to the game.

Tested with GOG; Steam should work the same way.

## macOS

- **macOS** with a native RimWorld 1.6 app.
- **Homebrew**. The setup command installs GNU Make, the .NET SDK, and Docker Desktop.

See [macOS](guides/macos.md) for the installation steps.

## Sidecar worker

- **Docker** or Docker Desktop.
- A SlopWorld checkout.

See [Sidecar worker](guides/sidecar.md) for setup and workspace mounts.

For the build toolchain and contributor checks, see [Build from source](build.md).
