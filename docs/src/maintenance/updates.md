# Updates and uninstallation

These steps apply to SlopWorld installed through your package manager.
For container updates, see [Sidecar mode](../installation/sidecar.md).
For a source checkout, see [Build from source](../development/build.md).

## Updating

See the [Changelog](../resources/changelog.md) for changes in each release.

To update, download the replacement packages for the components you installed and follow the
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

For a full local uninstall, close all tmux sessions, stop the daemon, and uninstall the mod.
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
