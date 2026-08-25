# macOS compatibility

macOS can run the RimWorld mod and host-facing parts of `slopd`, but it cannot run the
current agent sandbox. Bubblewrap and pasta require Linux mount, user, PID and network
namespaces. A native port therefore needs either an explicitly unsandboxed mode or a Linux
worker. The supported design should use one Docker Desktop sidecar as that worker; native
unsandboxed agents may exist only as a separately labelled development mode.

## Dependency boundary

The C# mod is mostly portable. Its X11 fullscreen and precise-scroll calls already gate on
`RuntimePlatform.LinuxPlayer`. macOS still needs platform-specific fullscreen settings,
endpoint/data/cache paths and Command-key behavior.

The Rust dependency graph is portable enough for a native host process: Axum, Tokio,
Alacritty Terminal, tmux and CPAL all support macOS, and CPAL selects CoreAudio there. These
external dependencies remain Linux-specific:

- `bwrap` constructs every non-host session and `pasta` supplies the default private network.
- `systemd-run` enforces per-agent limits; `systemctl` and journald serve installation and logs.
- `gio`/`gdbus`, Wayland/X11 clipboard programs and the ALSA device policy provide host UI and
  audio integration.
- The Makefile, launcher, game discovery, profile defaults and log paths name the Linux build.

`tools/check-reqs.json` should distinguish build, core runtime and optional integration
requirements. The C# build dependency is Mono's `csc`, matching the Makefile; the .NET SDK
is only needed for optional formatting and test tooling. The checker also lists unused
`pgrep` and treats configurable desktop/editor tools as required.

## Sidecar contract

The first implementation may run all of `slopd`, tmux, Bubblewrap, pasta and the agent CLIs in
one multi-architecture Linux container. RimWorld and the mod stay native:

```text
RimWorldMac + mod -> 127.0.0.1:7717 -> Docker port -> slopd/tmux/bwrap/pasta
                                                    -> selected host bind mounts
```

The container contract is:

- Publish `127.0.0.1:7717:7717`, make `slopd` bind `0.0.0.0:7717`, and require a non-empty
  token. `endpoint::url_for` already turns a wildcard bind into the loopback URL the mod needs.
- Mount the SlopWorld config directory rather than `endpoint.toml`; atomic replacement cannot
  replace a file mount. Persist config and session state across container replacement.
- Mount approved workspace roots at identical absolute paths on macOS and in the container.
  Paths cross the wire and are also used by file browsing, Git, search and file actions.
- Mount only named credential files/directories. A whole-home mount enlarges the effect of a
  daemon compromise, host errand or sandbox escape. Never mount the Docker socket by default.
- Discover the container resolver. The current `127.0.0.53` default assumes
  systemd-resolved and does not describe Docker Desktop DNS.

Nested Bubblewrap is the feasibility gate. Docker's default seccomp policy restricts namespace
syscalls. Test an unprivileged container first, then a narrow custom seccomp policy, then
`CAP_SYS_ADMIN`; do not make `--privileged` the default. Validate both arm64 and amd64 Docker
Desktop before treating sidecar mode as supported.

## Capability differences

The daemon must announce platform capabilities so the mod can hide or relabel unavailable
controls. Sidecar mode changes these contracts:

- `host` network shares the sidecar network, not macOS; Mac services use
  `host.docker.internal`.
- A single container can enforce one outer CPU/memory/PID budget. Existing per-agent systemd
  limits are unavailable without delegated cgroups or multiple containers.
- Host terminals are container shells. X11, Wayland, D-Bus, systemd, GPU, Linux audio and
  `slopworld-debug` presets are unavailable or need sidecar-specific replacements.
- Container replacement ends tmux sessions. Durable private state survives and sessions can be
  recreated, but daemon-only redeploy semantics do not.
- Host bind mounts retain macOS filesystem case and performance behavior.

Clipboard operations execute through the native game's system buffer in sidecar mode. Default
file opening should also move into the native mod on macOS using `open`; URL opening already uses
Unity's native API. Containerized `slopd` has no CoreAudio device, so the first sidecar release
reports jukebox playback as unsupported. Later playback can stream decoded local audio to the mod.

## Implementation order

1. Prove nested `bwrap` + `pasta`, tmux control mode, bind mounts and WebSocket reconnect on
   Apple Silicon.
2. Add platform/capability reporting and make unsupported settings impossible to select.
3. Add the sidecar image, launch configuration, token/path validation and container-aware DNS.
4. Add the macOS mod installer and profile launcher; align endpoint, data, cache and log paths.
5. Move file opening to the mod and add macOS input/fullscreen behavior.
6. Add Intel validation, container replacement recovery and an explicit unsupported-feature
   test matrix.

A later full-parity design can keep native `slopd` for launchd, CoreAudio and host integration
while a `SessionRuntime` backend drives the one Linux worker. That split is larger than the
full-daemon sidecar and should follow a working compatibility release.

## Sidecar status

`slopcar/` implements the first full-daemon worker on Debian trixie: an allowlisted
launcher, persistent config/state mounts, loopback-only publishing, runtime capabilities,
container-aware DNS, and a doctor that exercises pasta, Bubblewrap and tmux together. The proven
Docker profile runs as uid 1000 with every capability dropped, a read-only root, the
containers/common seccomp profile plus private-UTS hostname calls, `systempaths=unconfined`, and
`/dev/net/tun`; it does not use `--privileged` or `CAP_SYS_ADMIN`. Bubblewrap cannot create
the nested user namespace with Docker's `no-new-privileges`, so that flag is omitted.

Debian's official images publish both `linux/amd64` and `linux/arm64`; the launcher does not pin a
platform, so Apple Silicon does not need emulation. Linux Docker amd64 is verified. Docker Desktop
on Intel and Apple Silicon, native mod installation/input/fullscreen, desktop file opening, and
container-replacement reconnect remain compatibility work rather than verified support.
