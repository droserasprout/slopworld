# macOS compatibility status

The compatibility model and sidecar contract are in
[macOS compatibility](ops-macos-compatibility.md). This note tracks implementation
order and what has been verified.

## Implementation order

1. Prove nested `bwrap` + `pasta`, tmux control mode, bind mounts and WebSocket reconnect on
   Apple Silicon.
2. Add platform/capability reporting and make unsupported settings impossible to select.
3. Add the sidecar image, launch configuration, token/path validation and container-aware DNS.
4. Keep the macOS mod installer and profile launcher aligned with endpoint, data, cache and log
   paths.
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
platform, so Apple Silicon does not need emulation. Linux Docker amd64 is verified. The Makefile's
native macOS workflow covers mod build/install and Rust-mediated profile launch; Docker Desktop inside
a QEMU macOS guest, native input/fullscreen, desktop file opening, and container-replacement
reconnect remain compatibility work rather than verified support.
