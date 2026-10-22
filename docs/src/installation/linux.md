# Linux

Install SlopWorld on native Linux using an Arch, Debian/Ubuntu, or Fedora package.
For other setups, see [macOS](../installation/macos.md) or [Sidecar mode](../installation/sidecar.md).

## Requirements

- **x86_64 Linux** with systemd user services and scopes.
- **RimWorld 1.6** installed separately. Locate the folder containing the `RimWorldLinux` binary.
- **Runtime dependencies**, including Bubblewrap, passt, and tmux. The package manager installs these.

## Get binaries

Install a Linux binary package from the
[release downloads](https://github.com/droserasprout/slopworld/releases).
Each package includes the daemon, CLI, launcher, and RimWorld mod.
The commands below require `wget` and use version {{#constant release_version}}.

### Arch Linux

Download and install the Arch package:

```sh
wget https://github.com/droserasprout/slopworld/releases/download/{{#constant release_tag}}/slopworld-{{#constant release_version}}-x86_64.pkg.tar.zst
sudo pacman -U ./slopworld-{{#constant release_version}}-x86_64.pkg.tar.zst
```

<a id="debian--ubuntu"></a>

### Debian and Ubuntu

Download and install the Debian package:

```sh
wget https://github.com/droserasprout/slopworld/releases/download/{{#constant release_tag}}/slopworld-{{#constant release_version}}-amd64.deb
sudo apt install ./slopworld-{{#constant release_version}}-amd64.deb
```

The release package is built on Debian 13 (trixie). Your distribution must provide
its required library versions. If your release cannot satisfy those dependencies,
see [Build from source](../development/build.md#debian-packages) to build a package for your system.

### Fedora

Download and install the Fedora package:

```sh
wget https://github.com/droserasprout/slopworld/releases/download/{{#constant release_tag}}/slopworld-{{#constant release_version}}-x86_64.rpm
sudo dnf install ./slopworld-{{#constant release_version}}-x86_64.rpm
```

The RPM requires Fedora 43 library versions. To build for another Fedora release, see
[Fedora packages](../development/releases.md#fedora-packages).

## Attach the mod and launch

Run these commands as your normal user. Replace the game path
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
