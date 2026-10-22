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
The launcher leaves `Config/Prefs.xml` creation to RimWorld so first-run display
detection runs. It signals a missing preferences file through the game-only
`SLOPWORLD_INITIAL_AUDIO` environment value; the mod applies audio defaults after
initialization. Existing preferences remain authoritative even with mod-list reset.

Retain the profile lock through game exit on Linux/macOS; external-game detection
is Linux-only. `slopworld/peers.rs` owns authenticated client registration and
secret-free listing. `peer_probe.rs` owns authenticated discovery, `peer_store.rs`
owns private file publication, and `peer_prompt.rs` owns hidden input and terminal
restoration. A launch derives its profile from the already selected descriptor,
so a concurrent selection change cannot mix a peer with another profile.
The shared path resolver reads the client-owned selected name; daemon publication
always uses its own descriptor path. Native/sidecar catalogs and client selections
remain separate. Profiles follow peer names, with explicit path overrides taking
precedence. The resolved profile owns lock identity; explicit paths can share a
folder. Redirect Unity logs through the shared resolver unless an explicit game
`-logFile` was supplied, and create the log parent only for normal launch.
[Linux window integration](ui-window-fullscreen.md) owns window behavior.
