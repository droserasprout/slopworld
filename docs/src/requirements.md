# Requirements

## Linux

- **Linux** for the complete installer and launcher. You can use any modern Linux distribution.
- **systemd**
- **Bubblewrap** - for sandboxing.
- **passt (pasta)** - for networking
- **tmux** - terminal multiplexer.

## RimWorld

Use the native Linux build of RimWorld.
Set the `RIMWORLD` environment variable to the game directory.

Tests used the GOG version. The Steam version should work the same way.

## macOS

- **macOS** with a native RimWorld 1.6 app.
- **Homebrew**. The setup command installs GNU Make, the .NET SDK, and Docker Desktop.

See [macOS](guides/macos.md) for the installation steps.

## Sidecar worker

- **Docker** or Docker Desktop.
- A local copy of the SlopWorld repository.

See [Sidecar worker](guides/sidecar.md) for installation instructions and workspace mounts.

For the build toolchain and contributor checks, see [Build from source](build.md).
