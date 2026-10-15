# Profiles and launch

The Rust launcher owns profile seeding, locking, and game process lifetime.
`slopworld/cli.rs` owns the Clap command tree, generated help, and completions.
The launcher accepts only its own options and commands; it constructs the game's
arguments from the selected profile, log path, and window settings. Local commands
dispatch before game/profile discovery. Endpoint and mod-install modules execute
typed requests without parsing argv.
`slopworld/instance.rs` owns profile-keyed locks and Linux external-game detection.
[Mod profile isolation](mod-profile.md) owns in-game gating.

`slopworld/game_config.rs` owns the saved Linux game directory. Successful installation
remembers a canonical game parent only when it contains `RimWorld`. Explicit
arguments/environment override it; invalid saved configuration blocks fallback.
Install/removal share platform resolution, with macOS paths matching `mac/config.just`.
See [paths](ops-paths.md) and [game profiles](../docs/src/maintenance/game-profiles.md)
for locations and options.

Validate before seeding. Preserve existing mod lists unless reset is explicit; seeded
lists omit `<version>` so RimWorld cannot replace them on mismatch. Reject `=` in
resolved profile paths because the game parser splits on it. Print mode creates
nothing.
`profile init` and `profile reset` resolve the selected profile and hold its launcher
lock without requiring game discovery or a live endpoint. Reset replaces only the mod
list; normal launches preserve it. Both profile commands repair missing seeded files.
The launcher leaves `Config/Prefs.xml` creation to RimWorld so first-run display
detection runs. It signals a missing preferences file through the game-only
`SLOPWORLD_INITIAL_AUDIO` environment value; the mod applies audio defaults after
initialization. Existing preferences remain authoritative even with mod-list reset.

Retain the profile lock through game exit on Linux/macOS; external-game detection
is Linux-only. `slopworld/endpoints.rs` owns authenticated client registration and
secret-free listing. `peer_probe.rs` owns authenticated discovery, `peer_store.rs`
owns private file publication, and `peer_prompt.rs` owns hidden input and terminal
restoration. A launch derives its profile from the already selected descriptor,
so a concurrent selection change cannot mix a peer with another profile.
`connect` registers remote descriptors without reading or writing client selection;
`--endpoint` selects a peer for a launch. The reserved `local` peer is daemon-owned
and cannot be registered with `connect`.
The shared path resolver defaults to `local`; daemon publication always uses its
own descriptor path. Native/sidecar catalogs remain separate. Profiles follow peer names, with explicit path overrides taking
precedence. The resolved profile owns lock identity; explicit paths can share a
folder. Capture stdout and stderr in the shared resolved `Player.log`, replacing
it on each launch, and pass `-NoFileLog` to disable Waylith's separate logger.
Prepare the log only after profile validation and locking during normal launch.
[Linux window integration](ui-window-fullscreen.md) owns window behavior.

Linux launches set `FORCE_X11=1` for Waylith's GLFW initialization. Native X11 and
XWayland windows share the mod's EWMH fullscreen and decoration handling; native
Wayland windows do not expose that boundary. The renderer uses the game's Vulkan backend.
