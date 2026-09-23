# macOS compatibility status

The compatibility model and sidecar contract are in
[macOS compatibility](ops-macos-compatibility.md). This note tracks implementation
order and verified results.

## Sidecar status

`slopcar/` implements the first full-daemon worker on Debian trixie: an allowlisted
launcher, persistent config/state mounts, loopback-only publishing, runtime capabilities,
container-aware DNS, and a doctor that exercises pasta, Bubblewrap and tmux together. The proven
Docker profile runs as uid 1000 with every capability dropped, a read-only root, the
containers/common seccomp profile plus private-UTS hostname calls, `systempaths=unconfined`, and
`/dev/net/tun`.
It does not use `--privileged` or `CAP_SYS_ADMIN`. Bubblewrap cannot create
the nested user namespace with Docker's `no-new-privileges`, so the profile omits that flag.

Debian's official images provide both `linux/amd64` and `linux/arm64`.
The launcher does not fix the platform, so Apple Silicon does not need emulation. Linux Docker amd64 is verified. The Makefile's
native macOS workflow covers mod build/install and profile launch through Rust.
These areas still require compatibility work and verification:

- Docker Desktop inside a QEMU macOS guest.
- Native input and fullscreen.
- Desktop file opening.
- Reconnection after container replacement.

## Remaining validation

Prove the nested sandbox, tmux control mode, binds and reconnect on Docker Desktop for
Apple Silicon and Intel. Check native input/fullscreen, desktop file opening, and recovery
after container replacement. Linux Docker validation does not establish those results.
