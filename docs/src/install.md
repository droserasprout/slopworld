# Install

## Linux

Install the prerequisites from [Requirements](requirements.md).
Then run these commands:

```sh
git clone https://github.com/droserasprout/slopworld.git
cd slopworld
RIMWORLD=/path/to/RimWorld/game make install
slopworld
```

`RIMWORLD` is the directory that contains the native Linux game executable.
Use the launcher to give SlopWorld its own game profile.
The loading screen uses its bundled glyph atlas and does not install an operating-system font.

## macOS

Follow the [macOS installation guide](guides/macos.md).
This procedure installs the mod into the native Mac game and runs the daemon in Docker.

## Sidecar worker

Use a [sidecar worker](guides/sidecar.md) when the daemon and agent sandboxes should
run in Docker independently of the game host.
