# Linux

Install SlopWorld using your distribution's package manager. Install the packages
for the role of each machine. To run it in a container, see
[Sidecar mode](../installation/sidecar.md).

## Requirements

- **x86_64 Linux** with systemd user services and scopes.
- **RimWorld 1.7** installed separately. Locate the folder containing the `RimWorld` binary.
- **Runtime dependencies**, including Bubblewrap, passt, and tmux 3.7+. The package manager installs these.

## Interactive installer

Download `install.py` from the
[latest release](https://github.com/droserasprout/slopworld/releases/latest), then run:

```sh
python3 install.py
```

The installer requires Python 3.10 or newer, with no additional Python packages.
If Python is missing, install `python3` with apt or dnf, or `python` with pacman.
It selects Debian/Ubuntu, Fedora, or Arch packages using the distribution identity
and ancestry, including derivatives such as CachyOS. For unrecognized distributions,
it detects apt, dnf, or pacman; ambiguous detection stops with an error. Package
library requirements and distribution-specific prerequisites below still apply.

Use Up/Down to move between component checkboxes and Space to toggle them.
Press Enter to apply changes, or Q/Esc to cancel. If terminal controls are
unavailable, the installer falls back to numbered toggles.
Installed components are preselected and their versions are shown alongside the
target release. **Unchecking an installed component uninstalls it.** Selecting
`slopd` also selects its required `slopctl` package. On ARM64, only `slopcar` is
available, on Debian/Ubuntu and Fedora.

The installer downloads the selected packages and checksums from one GitHub release
into a private directory under `/tmp`, verifies every package, and invokes the
system package manager. Run it as your normal user; it uses `sudo` for installation.
The package manager shows its transaction and resolves runtime dependencies.
Temporary downloads are removed on completion, failure, or cancellation.
Checksums detect damaged downloads; they do not independently establish provenance.
See [release verification](../development/releases.md#verify-a-download) for attestation checks.

Run it again to update selected components. Add `--version VERSION` to an
explicit `--install` command to select a specific stable release. Installation does not attach the mod or start
services; continue with [Attach the mod and launch](#attach-the-mod-and-launch).

### Without the menu

Explicit actions skip the menu and leave unmentioned components unchanged:

```sh
python3 install.py --install slopworld slopd
python3 install.py --remove slopd slopctl
python3 install.py --install slopcar --remove slopd
```

`--install` installs or updates; selecting `slopd` also installs/updates `slopctl`.
To remove `slopctl` while `slopd` is installed, remove both. Removal preserves user
configuration and does not request dependency cleanup or purge. Removing packages
does not detach an already attached game mod.

Running without arguments opens the menu. Any arguments select noninteractive
mode and require `--install` or `--remove` (except `--help`). Package-manager
prompts are accepted automatically; existing configuration is retained. Sudo uses
`-n` and fails if authentication is needed, so authenticate beforehand with
`sudo -v` or run with sufficient privileges. `--no-tui` is an optional explicit
marker for this mode. Removal-only commands do not contact GitHub. Mixed commands verify and
install downloads before removing packages. These are separate transactions: if
removal fails, successful installations remain applied.

## Get binaries

Install a Linux binary package from the
[release downloads](https://github.com/droserasprout/slopworld/releases).
| Package | Contents | Install on |
| --- | --- | --- |
| `slopworld` | Game launcher, mod, assets and OST | Game desktop |
| `slopd` | Daemon and user service; requires `slopctl` | Native agent server |
| `slopctl` | CLI for local or remote daemons | Server, administration machine, or CI |
| `slopcar` | Container launcher | Sidecar host |

For a local native setup, install `slopworld`, `slopd`, and `slopctl` together.
For a remote game client, install only `slopworld`. For a sidecar game host,
install `slopworld` and `slopcar`; `slopctl` is optional on that host.
The commands below install the local native setup.
The commands below require `wget` and use version {{#constant release_version}}.

### Arch Linux

Download and install the Arch packages:

```sh
for package in slopworld slopd slopctl; do
  wget "https://github.com/droserasprout/slopworld/releases/download/{{#constant release_tag}}/${package}-{{#constant release_version}}-x86_64.pkg.tar.zst"
done
sudo pacman -U ./slopworld-{{#constant release_version}}-x86_64.pkg.tar.zst ./slopd-{{#constant release_version}}-x86_64.pkg.tar.zst ./slopctl-{{#constant release_version}}-x86_64.pkg.tar.zst
```

### Ubuntu 26.10+

Download and install the deb packages:

```sh
for package in slopworld slopd slopctl; do
  wget "https://github.com/droserasprout/slopworld/releases/download/{{#constant release_tag}}/${package}-{{#constant release_version}}-amd64.deb"
done
sudo apt install ./slopworld-{{#constant release_version}}-amd64.deb ./slopd-{{#constant release_version}}-amd64.deb ./slopctl-{{#constant release_version}}-amd64.deb
```

Older releases don't have tmux 3.7 in repositories or backports.

<a id="debian--ubuntu"></a>

### Debian 13+

Download and install the deb packages:

```sh
echo 'deb https://deb.debian.org/debian trixie-backports main' | sudo tee /etc/apt/sources.list.d/backports.list
sudo apt update
sudo apt install tmux/trixie-backports

for package in slopworld slopd slopctl; do
  wget "https://github.com/droserasprout/slopworld/releases/download/{{#constant release_tag}}/${package}-{{#constant release_version}}-amd64.deb"
done
sudo apt install ./slopworld-{{#constant release_version}}-amd64.deb ./slopd-{{#constant release_version}}-amd64.deb ./slopctl-{{#constant release_version}}-amd64.deb
```

### Fedora

Download and install the Fedora packages:

```sh
for package in slopworld slopd slopctl; do
  wget "https://github.com/droserasprout/slopworld/releases/download/{{#constant release_tag}}/${package}-{{#constant release_version}}-x86_64.rpm"
done
sudo dnf install ./slopworld-{{#constant release_version}}-x86_64.rpm ./slopd-{{#constant release_version}}-x86_64.rpm ./slopctl-{{#constant release_version}}-x86_64.rpm
```

The RPM requires Fedora 43 library versions. To build for another Fedora release, see [Fedora packages](../development/releases.md#fedora-packages).

## Optional integrations

Packages list these as optional dependencies (`optdepends` on Arch, `Suggests`
on Debian and Fedora). Install the tools for the features you use:

| Feature | Arch | Debian/Ubuntu | Fedora |
| --- | --- | --- | --- |
| Preferred pager | `moor` | `moor` | not packaged |
| Alternative highlighters | `highlight`, `python-pygments` | `highlight`, `python3-pygments` | `highlight`, `python3-pygments` |
| Desktop application associations | `glib2` | `libglib2.0-bin` | `glib2` |
| Native Open With chooser | `python-gobject`, `gtk3` | `python3-gi`, `gir1.2-gtk-3.0` | `python3-gobject`, `gtk3` |
| Portal chooser fallback | `xdg-desktop-portal` | same | same |
| X11 clipboard fallback | `xsel` | same | same |
| Process inspection fallback | `procps-ng` | `procps` | `procps-ng` |
| Spotify playback | `ncspot` | not packaged | not packaged |
| Song recognition | `songrec` | not packaged | not packaged |

## Attach the mod and launch

Run these commands as your normal user. Replace the game path
with your RimWorld installation directory:

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

Install `slopd` and `slopctl` on the server, and `slopworld` on the client.
Attach the mod to the client's RimWorld installation as above. The remote server runs `slopd` and your agents; the client
runs the game. This example uses SSH host `tank` and server IP `192.168.1.10`.
Replace them with your server's hostname and reachable IP address.

On the remote server, start the daemon and set its listener address:

```sh
# On remote server
systemctl --user daemon-reload
systemctl --user enable --now slopd.service

slopctl config set daemon.bind 192.168.1.10:7717
systemctl --user restart slopd.service
```

On the server, run `slopctl paths endpoint` to locate the endpoint descriptor.
Read its `token` field and enter that value at the client’s hidden key prompt below.
See [Paths and files](../reference/paths.md) if you use custom configuration roots.

On the client, register the daemon:

```sh
slopworld connect tank http://192.168.1.10:7717
slopworld --endpoint tank
```

The first command saves the connection as `endpoints/tank.toml`. Launching with
`--endpoint tank` creates its separate `profiles/tank` game profile.
Use `slopworld endpoints` to list saved peers.

### Standalone CLI

Install only `slopctl` on an administration machine or CI runner. Set `SLOPD_URL`
and `SLOPD_TOKEN` for the remote daemon, then use commands such as `slopctl status`
and `slopctl task wait ID`. No local daemon or game is required. Local log commands
still read that machine's logs.

## Next steps

Continue with [Quickstart](../getting-started/quickstart.md) to prepare your agent CLI, add a project,
and run your first task.

<a id="updating"></a>
<a id="removing"></a>

See [Updates and uninstallation](../maintenance/updates.md) to update or remove SlopWorld.
