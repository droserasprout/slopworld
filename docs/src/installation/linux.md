# Linux

Install SlopWorld on native Linux using binary Arch/DEB/RPM packages.
For other setups, see [macOS](../installation/macos.md) or [Sidecar mode](../installation/sidecar.md).

## Requirements

- **x86_64 Linux** with systemd user services and scopes.
- **RimWorld 1.6** installed separately. Locate the folder containing the `RimWorldLinux` binary.
- **Runtime dependencies**, including Bubblewrap, passt, and tmux 3.7+. The package manager installs these.

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

### Ubuntu 26.10+

Download and install the deb package:

```sh
wget https://github.com/droserasprout/slopworld/releases/download/{{#constant release_tag}}/slopworld-{{#constant release_version}}-amd64.deb
sudo apt install ./slopworld-{{#constant release_version}}-amd64.deb
```

Older releases don't have tmux 3.7 in repositories or backports.

<a id="debian--ubuntu"></a>

### Debian 13+

Download and install the deb package:

```sh
echo 'deb https://deb.debian.org/debian trixie-backports main' | sudo tee /etc/apt/sources.list.d/backports.list
sudo apt update
sudo apt install tmux/trixie-backports

wget https://github.com/droserasprout/slopworld/releases/download/{{#constant release_tag}}/slopworld-{{#constant release_version}}-amd64.deb
sudo apt install ./slopworld-{{#constant release_version}}-amd64.deb
```

### Fedora

Download and install the Fedora package:

```sh
wget https://github.com/droserasprout/slopworld/releases/download/{{#constant release_tag}}/slopworld-{{#constant release_version}}-x86_64.rpm
sudo dnf install ./slopworld-{{#constant release_version}}-x86_64.rpm
```

The RPM requires Fedora 43 library versions. To build for another Fedora release, see [Fedora packages](../development/releases.md#fedora-packages).

## Optional integrations

Packages list these as optional dependencies (`optdepends` on Arch, `Suggests`
on Debian and Fedora). Install the tools for the features you use:

| Feature | Arch | Debian/Ubuntu | Fedora |
| --- | --- | --- | --- |
| Alternative highlighters | `highlight`, `python-pygments` | `highlight`, `python3-pygments` | `highlight`, `python3-pygments` |
| Desktop application associations | `glib2` | `libglib2.0-bin` | `glib2` |
| Native Open With chooser | `python-gobject`, `gtk3` | `python3-gi`, `gir1.2-gtk-3.0` | `python3-gobject`, `gtk3` |
| Portal chooser fallback | `xdg-desktop-portal` | same | same |
| Wayland clipboard | `wl-clipboard` | `wl-clipboard` | `wl-clipboard` |
| X11/XWayland clipboard | `xclip` or `xsel` | same | same |
| Process inspection fallback | `procps-ng` | `procps` | `procps-ng` |
| Spotify playback | `ncspot` | not packaged | not packaged |
| Song recognition | `songrec` | not packaged | not packaged |

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

## Remote connection

Install SlopWorld on both machines and attach the mod to the client's RimWorld
installation as above. The remote server runs `slopd` and your agents; the client
runs the game. This example uses SSH host `tank` and server IP `192.168.1.10`.
Replace them with your server's hostname and reachable IP address.

On the remote server, start the daemon once to create its configuration, then stop
it before editing:

```sh
# On remote server
systemctl --user daemon-reload
systemctl --user enable --now slopd.service
systemctl --user stop slopd.service
nano ~/.config/slopworld/config.toml
```

In the existing `[daemon]` section, set the listener address:

```toml
[daemon]
bind = "192.168.1.10:7717"
```

Keep the other settings, including the token. Start the daemon with the new address:

```sh
systemctl --user start slopd.service
```

On the client, copy the endpoint descriptor and edit its `url` to
`http://192.168.1.10:7717`, keeping its token:

```sh
# On client
mkdir -p ~/.config/slopworld
scp tank:.config/slopworld/endpoint.toml ~/.config/slopworld/tank.toml
nano ~/.config/slopworld/tank.toml

# Run the game with a separate profile for this server
SLOPD_ENDPOINT="$HOME/.config/slopworld/tank.toml" \
SLOPWORLD_PROFILE="$HOME/.local/share/slopworld/profile-tank" \
slopworld
```

## Next steps

Continue with [Quickstart](../getting-started/quickstart.md) to prepare your agent CLI, add a project,
and run your first task.

<a id="updating"></a>
<a id="removing"></a>

See [Updates and uninstallation](../maintenance/updates.md) to update or remove SlopWorld.
