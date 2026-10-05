# Install

## Linux

Install the prerequisites from [Requirements](requirements.md).
Then run these commands:

```sh
git clone https://github.com/droserasprout/slopworld.git
cd slopworld
RIMWORLD=/path/to/RimWorld/game just install
slopworld
```

`RIMWORLD` is the directory that contains the native Linux game executable.
After mod installation succeeds, the installer saves the game directory as the launcher
default in [game.toml](reference/paths.md#launcher-configuration).
The launcher checks `--game`, then `SLOPWORLD_GAME`, then the saved path,
then `~/RimWorld/game` and standard GOG and Steam paths.
If you move the game, run `RIMWORLD=/new/path/to/game just install-mod`.
An invalid saved path reports an error rather than selecting another installation.
Each successful mod installation into a Linux game's `Mods` directory replaces the
saved default. The launcher uses a separate [game profile](guides/game-profiles.md).

## Debian and Ubuntu

Build a package with [`just pkg-debian`](build.md#debian-packages), then install it:

```sh
sudo apt install ./dist/debian/slopworld_<version>-1_amd64.deb
```

Replace the filename with the generated package (use `arm64` for that architecture).
As your normal user, attach the packaged mod and start the daemon:

```sh
RIMWORLD=/path/to/RimWorld/game
ln -sT /usr/share/slopworld/SlopWorld "$RIMWORLD/Mods/SlopWorld"
systemctl --user daemon-reload
systemctl --user enable --now slopd.service
slopworld --game "$RIMWORLD"
```

Enable SlopWorld in the game's mod list. If a source installation left a unit at
`~/.config/systemd/user/slopd.service`, remove that unit before reloading so the
packaged unit can take effect. After upgrades, run `systemctl --user daemon-reload`
and `systemctl --user try-restart slopd.service`.
Before removing the package, run `systemctl --user disable --now slopd.service`.
Then remove the mod symlink and reload the user units. Per-user data is preserved.
The installed `/usr/share/doc/slopworld/README.Debian` also contains these steps.

## macOS

Follow the [macOS installation guide](guides/macos.md).
This procedure installs the mod into the native Mac game and runs the daemon in Docker.

## Sidecar worker

Use a [sidecar worker](guides/sidecar.md) when the daemon and agent sandboxes should
run in Docker independently of the game host.
