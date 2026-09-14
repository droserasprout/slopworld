# macOS compatibility status

The compatibility model and sidecar contract are in
[macOS compatibility](ops-macos-compatibility.md). This note tracks implementation
order and what has been verified.

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

## Remaining validation

Prove the nested sandbox, tmux control mode, binds and reconnect on Docker Desktop for
Apple Silicon and Intel. Check native input/fullscreen, desktop file opening, and recovery
after container replacement. Linux Docker validation does not establish those results.
