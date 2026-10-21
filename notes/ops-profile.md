# Profiles and launch

The Rust launcher owns profile seeding, locking, and game process lifetime.
`slopworld/instance.rs` owns profile-keyed locks and Linux external-game detection.
[Mod profile isolation](mod-profile.md) owns in-game gating.

`slopworld/game_config.rs` owns the saved Linux game directory. Successful installation
remembers a canonical game parent only when it contains `RimWorldLinux`. Explicit
arguments/environment override it; invalid saved configuration blocks fallback.
Install/removal share platform resolution, with macOS paths matching `mac/config.just`.
See [paths](ops-paths.md) and [game profiles](../docs/src/maintenance/game-profiles.md)
for locations and options.

Validate before seeding. Preserve existing mod lists unless reset is explicit; seeded
lists omit `<version>` so RimWorld cannot replace them on mismatch. Reject `=` in
resolved profile paths because the game parser splits on it. Print mode creates
nothing.

Retain the profile lock through game exit on Linux/macOS; external-game detection
is Linux-only. Native/sidecar defaults are selected by `--sidecar`, not endpoint or
profile overrides. Explicit paths can share a folder; endpoint does not select lock
identity. Redirect Unity logs through the shared resolver unless an explicit game
`-logFile` was supplied, and create the log parent only for normal launch.
[Linux window integration](ui-window-fullscreen.md) owns window behavior.
