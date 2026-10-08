# Updates and uninstallation

These steps apply to native Linux packages. For other installations, see
[macOS](../deployment/macos.md), [Sidecar mode](../deployment/sidecar.md), or
[Build from source](../development/build.md).

## Updating

To update, download the replacement package and follow the
[package installation](../getting-started/install.md#get-binaries) steps. Refresh the installed
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

Before removing the package, close all tmux sessions, stop the daemon, and uninstall the mod:

```sh
# Disable daemon, unlink mod
systemctl --user disable --now slopd.service
slopworld mod uninstall

# Uninstall package

## Debian / Ubuntu
sudo apt remove slopworld

## Arch
sudo pacman -R slopworld
```

Per-user configuration, sessions, and saves are preserved; see [Paths and files](../reference/paths.md).
