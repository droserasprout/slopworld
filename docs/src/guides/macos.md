# macOS

macOS support runs the daemon and sandbox inside a Docker Desktop sidecar. RimWorld
and the mod run natively; agents run in a Linux container.

## Prerequisites

- Docker Desktop for macOS (arm64 or amd64)
- GNU Make (`brew install make`)

## Install

```sh
gmake mac-setup
open -a Docker
gmake mac
```

`mac-setup` builds the Debian sidecar image and installs the native mod.

`MAC_RIMWORLD` defaults to the GOG bundle at `~/Documents/RimWorld.app`. Override it
for a Steam or other install. The first start creates a random daemon token and
publishes port 7718 on Mac loopback only.

`mac-run` launches the native game directly; the Linux `slopworld` launcher is not used
on macOS.

## How the sidecar works

```
Native macOS (RimWorld + mod) → 127.0.0.1:7718 → Docker → slopd / tmux / bwrap / pasta
```

The mod connects to the daemon inside the container over the published port.
Configuration and session state are mounted from the host so they survive container
replacement.

Workspace directories must be mounted at identical absolute paths on both macOS and
inside the container. Paths cross the wire and are used by file browsing, Git, search,
and file actions.

Credential files are mounted individually. The container never mounts the full home
directory or the Docker socket.

## Differences from Linux

| Feature | Linux | macOS sidecar |
| --- | --- | --- |
| Network | `host` shares the host stack | `host` shares the sidecar network; Mac services use `host.docker.internal` |
| Per-agent limits | systemd cgroups | Unavailable without delegated cgroups |
| Jukebox | Daemon decodes MP3 to host audio | Unsupported; the OST plays through RimWorld's native music manager instead |
| Host terminals | Native host shells | Container shells |
| Clipboard | Native Wayland/X11 | Routed through the game's system buffer |
| Debug preset | Functional | X11, Wayland, D-Bus, systemd, GPU, and Linux audio escapes are unavailable |

Container replacement ends tmux sessions. Private state survives and sessions are
recreated, but in-flight work is lost.

## Updating

Pull changes and run `gmake mac` again. This rebuilds the sidecar, reinstalls the mod,
and launches the game.
