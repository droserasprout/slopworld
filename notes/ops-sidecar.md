# Linux sidecar architecture boundary

The Linux game/mod talks to a Linux `slopcar` container running the daemon, tmux,
and agent sandboxes. The worker depends on Linux Bubblewrap/pasta. Container images
support amd64/arm64; the host game still requires a supported Linux game build.

`slopcar/` owns container tooling; its independent Rust crate owns the host launcher
and embeds the seccomp policy. Image builds still need a checkout.
`slopcar/src/cli.rs` owns the Clap command tree and generated help. It preserves
credential mount order across read-only/read-write options and forwards every
argument after `logs` verbatim to Docker; use `slopcar help logs` for CLI help.
Local help, completions, notices, and paths bypass container settings and Docker discovery.
Mount policy and filesystem validation remain in their execution owners.

Image builds are explicit. The default image is the published tag matching the
launcher crate version. Doctor and fresh starts pull the selected `SLOPCAR_IMAGE`
only when absent, preserving installed/custom images and agent CLIs. Invalid mount
plans fail before pulls or persistent-state creation.

`tools/release/sidecar.py` coordinates independent distro packages; native package
format owners stage a separate `slopcar` product through `staging.stage_sidecar`.
It contains no mod, `slopd` binary, or systemd unit; the daemon runs from the container image.
Hosted native-architecture jobs
hand packages to the final release job for checksums and attestations. The minimum
host-library support baseline is Debian 13; `packaging/debian/Dockerfile` owns that
build environment. Other formats use native builders and dependency discovery.
Do not introduce an older-libc or musl compatibility target. Docker/kernel sandbox
capabilities remain separate from the library baseline.

Host storage uses the common application roots and explicit sidecar mode;
`shared/application_paths.rs` owns selection. Host root overrides select mount
sources and never become container environment paths. Workspace paths match on
host/container. Container mount/security policy belongs to
[slopcar](../slopcar/README.md), and setup/lifecycle to
[sidecar](../docs/src/installation/sidecar.md).
The launcher supplies Linux host IDs through `PUID`/`PGID`. The root entrypoint
updates ordinary account databases, then drops
privileges and capabilities before running the daemon or doctor as `slop`.
The writable container root and minimal startup capabilities belong to the sidecar
security policy. Shells and health checks must explicitly select `slop`, since
Docker's configured container user is root. Persistent directories retain mode
0700 and tmpfs ownership matches the selected application credentials. Account
initialization must not recursively chown mounted credentials or state.
Container replacement ends tmux processes while
persistent private state remains;
this differs from daemon-only redeployment.

Sidecar capabilities disable desktop opening, audio playback, and per-session limits.
Clipboard operations, including PRIMARY and image reads, belong to the Linux game
host. The mod can play its bundled OST through RimWorld while radio/Spotify daemon
playback is unavailable.
