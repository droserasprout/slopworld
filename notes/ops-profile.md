# Profile and launch boundary

The Rust `slopworld` launcher owns profile seeding and process lifetime.
`slopworld/instance.rs` owns locking and external-game detection. Linux and macOS targets
share it. The mod only patches when the profile marker exists. Launching the game directly bypasses this requirement.

Profile mod-list seeding omits `<version>`: RimWorld can discard a mismatched versioned list
and re-enable expansions. Reject `=` in save-data paths because the game splits the argument
on it. Reset is explicit. Ordinary launch must preserve existing profile choices.
Validate launch paths before seeding; print mode must leave profiles untouched.
Resolve existing ancestors for absent profiles so seeding cannot change the lock identity.

The launcher waits rather than execs so its service lifetime matches the game. Hold the
profile-keyed kernel lock through exit and also detect games launched outside the launcher.
Separate sidecar/native profiles must not share saves merely because endpoints coincide.

Profile refusal has three independent gates: Harmony registration, startup def mutation and
XML patch operations. The game still loads new defs when patching is refused, so each entry
point needs its own guard. The game loads assemblies before XML and runs static constructors afterward.

See the [paths reference](../docs/src/reference/paths.md) for paths and overrides.
See [fullscreen](ui-window-fullscreen.md) for window behavior.
