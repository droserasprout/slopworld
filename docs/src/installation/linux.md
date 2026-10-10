# Linux

Install SlopWorld on native Linux using an Arch or Debian/Ubuntu package.
For other setups, see [macOS](../installation/macos.md) or [Sidecar mode](../installation/sidecar.md).

## Requirements

- **x86_64 Linux** with systemd user services and scopes.
- **RimWorld 1.6** installed separately. Locate the folder containing the `RimWorldLinux` binary.
- **Runtime dependencies**, including Bubblewrap, passt, and tmux. The package manager installs these.

## Get binaries

Install a Linux binary package from the
[release downloads](https://github.com/droserasprout/slopworld/releases).
Each package includes the daemon, CLI, launcher, and RimWorld mod.

### Arch Linux

The examples below use version {{#constant release_version}}; substitute the version you downloaded.

Download `slopworld-{{#constant release_version}}-x86_64.pkg.tar.zst` from the release, then run
this command in your download directory:

```sh
sudo pacman -U ./slopworld-{{#constant release_version}}-x86_64.pkg.tar.zst
```

<a id="debian--ubuntu"></a>

### Debian and Ubuntu

Download `slopworld-{{#constant release_version}}-amd64.deb` from the release, then run
this command in your download directory:

```sh
sudo apt install ./slopworld-{{#constant release_version}}-amd64.deb
```

The release package is built on Debian 13 (trixie). Your distribution must provide
its required library versions. If your release cannot satisfy those dependencies,
see [Build from source](../development/build.md#debian-packages) to build a package for your system.

## Attach the mod and launch

For either package, run these commands as your normal user. Replace the game path
with your native Linux RimWorld installation:

```sh
# Install the mod
RIMWORLD=/path/to/RimWorld/game
slopworld mod install --game "$RIMWORLD"

# Start the daemon
systemctl --user daemon-reload
systemctl --user enable --now slopd.service

# Launch SlopWorld
slopworld
```

The installer replaces the existing SlopWorld mod and saves the game directory
as the launcher default. The source defaults to `/usr/share/slopworld/SlopWorld`;
use `--source DIR` for another mod tree.

Always launch with `slopworld`. The mod uses a separate
[game profile](../maintenance/game-profiles.md); keep it disabled in your vanilla profile.

Continue with [Quickstart](../getting-started/quickstart.md) to prepare your agent CLI, add a project,
and run your first task.

<a id="updating"></a>
<a id="removing"></a>

See [Updates and uninstallation](../maintenance/updates.md) to update or remove SlopWorld.
