# macOS architecture boundary

The native game/mod talks to a Linux `slopcar` container running the daemon, tmux and agent
sandboxes. Bubblewrap/pasta cannot become native macOS backends without a different runtime.
See [status](ops-macos-compatibility-status.md) for verified versus pending support.

Persist config/state as directories, not individual file binds that block atomic replacement.
Workspace paths must match on host and container because files/Git/search/errands share them.
Add permitted workspace and credential mounts to the allowlist.
Never mount the entire home directory or Docker socket by default.
Publish on host loopback with a nonempty token.
Discover DNS inside the container.

Capabilities are part of the contract.
Container host shells and networking differ from macOS host shells and networking.
Per-agent systemd limits and Linux desktop/audio presets are unavailable.
Container replacement ends tmux processes even though private state persists, unlike a daemon
redeployment.
Clipboard uses the native game.
Native file opening, input, and fullscreen still need platform checks. OST can use RimWorld audio while daemon jukebox playback is unavailable.

The nested-sandbox Linux Docker profile already works without CAP_SYS_ADMIN. Do not infer
Docker Desktop support from that result or widen container privilege without evidence.
