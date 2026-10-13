# macOS architecture boundary

The native game/mod talks to a Linux `slopcar` container running the daemon, tmux,
and agent sandboxes. The current worker depends on Linux Bubblewrap/pasta; a native
macOS worker would require a different runtime backend. Multi-architecture images
support amd64/arm64, which does not itself establish platform validation.

`mac/` owns the native macOS workflow through its separate justfile; shared
container tooling remains in `slopcar/`. Its independent Rust crate owns the
host launcher and embeds the seccomp policy; image builds still need a checkout.
Image builds are explicit. Install, doctor, and launch consume the selected
`SLOPCAR_IMAGE` without rebuilding it, preserving user-installed agent CLIs.

Host storage uses the common application roots and explicit sidecar mode;
`shared/application_paths.rs` owns selection. Host root overrides select mount
sources and never become container environment paths. Workspace paths match on
host/container. Container mount/security policy belongs to
[slopcar](../slopcar/README.md), and setup/lifecycle to
[macOS](../docs/src/installation/macos.md) and [sidecar](../docs/src/installation/sidecar.md).
Linux launch credentials match the invoking host UID/GID; Docker Desktop uses
1000:1000. Persistent directories retain mode 0700 and tmpfs ownership matches
the selected credentials. Container replacement ends tmux processes while
persistent private state remains;
this differs from daemon-only redeployment.

Sidecar capabilities disable daemon clipboard, desktop opening, audio playback, and
per-session limits. Ordinary text clipboard operations use RimWorld/Unity fallback;
PRIMARY and daemon clipboard integration are unavailable. The mod can play its
bundled OST through RimWorld while radio/Spotify daemon playback is unavailable.
Native platform behavior requires platform-specific evidence, separate from runtime
capability declarations.
