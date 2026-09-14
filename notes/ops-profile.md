# Profile and launch boundary

The Rust `slopworld` launcher owns profile seeding, process lifetime and single-instance
locking. Linux and macOS targets share it. The mod only patches when the profile marker
exists; launching the game directly bypasses this contract.

Profile mod-list seeding omits `<version>`: RimWorld can discard a mismatched versioned list
and re-enable expansions. Reject `=` in save-data paths because the game splits the argument
on it. Reset is explicit; ordinary launch must preserve existing profile choices.

The launcher waits rather than execs so its service lifetime matches the game. Hold the
profile-keyed kernel lock through exit and also detect games started outside the launcher.
Separate sidecar/native profiles must not share saves merely because endpoints coincide.

Profile refusal has three independent gates: Harmony registration, startup def mutation and
XML patch operations. New added defs still exist when patching is refused, so entry points
need their own guard. Assemblies load before XML, static constructors afterward.

Paths and overrides live in the [paths reference](../docs/src/reference/paths.md);
window behavior is in [fullscreen](ui-window-fullscreen.md).
