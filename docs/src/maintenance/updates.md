# Updates and uninstallation

These steps apply to SlopWorld installed through your package manager.
For container updates, see [Sidecar mode](../installation/sidecar.md).
For a source checkout, see [Build from source](../development/build.md).

## Updating

See the [Changelog](../resources/changelog.md) for changes in each release.

To update just the browser workspace, stop its launcher with Ctrl+C and run the
downloaded [release installer](../installation/linux.md#interactive-installer).

```sh
sudo -v
python3 install.py --install slopweb
```

Run `slopweb` again and open the URL it prints. The launcher reuses matching
files in its private cache. The daemon can keep running during a web-only update.

For the game client and daemon, download the new packages for your installed components and follow the
[package installation](../installation/linux.md#get-binaries) steps. Refresh the installed
mod, then reload and restart the daemon as your
normal user:

```sh
# Update the mod
slopworld mod install

# Restart the daemon
systemctl --user daemon-reload
systemctl --user try-restart slopd.service
```

Your existing agent/shell sessions in tmux keep running.

## Removing

To remove just the browser workspace, stop its launcher with Ctrl+C and use
the downloaded installer.

```sh
sudo -v
python3 install.py --remove slopweb
```

Package removal preserves the private game-derived cache. To reclaim its disk
space, run `slopweb paths web-cache` before uninstalling and delete that
directory after removal. Other SlopWorld packages and daemon sessions stay in place.

To remove the local game client and daemon, close all tmux sessions, stop the daemon, and uninstall the mod.
On client-only or server-only machines, run only the steps for installed components:

```sh
# Disable daemon, unlink mod
systemctl --user disable --now slopd.service
slopworld mod uninstall

# Uninstall package

## Debian / Ubuntu
sudo apt remove slopworld slopd slopctl

## Arch
sudo pacman -R slopworld slopd slopctl

## Fedora
sudo dnf remove slopworld slopd slopctl
```

Per-user configuration, sessions, and saves are preserved; see [Paths and files](../reference/paths.md).
