# Profile and launch boundary

The Rust launcher owns profile seeding and game process lifetime.
`slopd/src/bin/slopworld/instance.rs` owns per-profile locking and Linux external-game
detection. The mod owns [profile-specific gating](mod-profile.md).
`slopworld/game_config.rs` owns the saved Linux game directory. The mod installer
remembers the canonical parent of the destination Mods directory atomically after
installation succeeds, when that parent contains `RimWorldLinux`. Other layouts
leave the Linux default unchanged. Explicit arguments and environment override the
saved path; invalid saved configuration blocks fallback to a different game.
Mod installation and removal resolve --game, then RIMWORLD, then platform defaults.
Linux uses the launcher defaults; macOS uses MAC_RIMWORLD or the app bundle
location shared with mac/config.just.
An explicit game directory also supports non-Linux layouts containing Mods.

Seeding preserves an existing mod list unless reset is explicit. Seeded lists omit
`<version>` so RimWorld cannot discard a mismatched list and re-enable expansions.
Resolved profile paths reject `=` because the game's argument parser splits on it.
Launch validation precedes seeding, and print mode never seeds.

The launcher retains a profile-keyed file lock through game exit on Linux and macOS.
Direct-game process detection is Linux-only. Native and sidecar profiles have separate
defaults selected by `--sidecar`; profile and endpoint environment overrides
do not select a mode. Explicit profile paths can select the same folder. The endpoint does not
choose lock identity.

User setup belongs to [game profiles](../docs/src/maintenance/game-profiles.md), paths to
[the path reference](../docs/src/reference/paths.md), and window behavior to
[Linux windowing](ui-window-fullscreen.md).

The launcher redirects Unity’s game log to `<CACHE>/Player.log`, with
`SLOPWORLD_GAME_LOG` taking precedence. Sidecar mode uses
`<CACHE>/sidecar/Player.log`. Explicit game `-logFile` arguments are
forwarded without adding another log argument. Print mode creates no directories;
normal launch creates the selected log parent before starting Unity.
