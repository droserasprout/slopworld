# Profile and launcher

RimWorld keeps saves, prefs and the mod list in one folder per install, so this
mod gets a save-data folder of its own plus a launcher that makes it.

`slopd/src/bin/slopworld.rs` finds the Linux game (`--game`, `$SLOPWORLD_GAME`, four
usual paths) or accepts an explicit executable with `--game-exe`, and finds the profile
(`--profile`, `$SLOPCAR_PROFILE`, `$SLOPWORLD_PROFILE`, XDG). It seeds the profile and
waits on the game with `-savedatafolder=<profile>`; native macOS runs provide the app's
executable, working directory and Mods directory explicitly.

`--init-profile` performs only the profile seeding step. The macOS Makefile target uses
that mode, so profile creation and launch share the same Rust implementation and tests.
Our flags are `--long`, the game's are `-single`, so an unknown `--word` is a typo
rather than something to forward; `--` ends ours. `--no-window-fix` omits the
defaults for an alternate windowing setup.

Seeding writes `Config/ModsConfig.xml` (two mods, `knownExpansions` naming all
five) only if absent; `--reset` overwrites. Two traps:

- **No `<version>` element.** The game compares one when present, throws the whole
  list away on mismatch, and rebuilds it with every expansion on.
- `-savedatafolder` is split on `=` into exactly two halves, so a profile path
  containing one is refused on the way in rather than silently ignored.

The launcher **waits on** the game rather than exec'ing. Waiting also makes the
launcher's lifetime the game's, which is what `slopworld-game.service` reports.
It cannot be a script: a shebang puts `/bin/sh` in `argv[0]`.

The launcher holds a profile-keyed kernel file lock for its entire lifetime. A second
invocation for that profile refuses before seeding or starting RimWorld; another profile may
run beside it. The lock is released automatically when the owner exits. Before launch it also
refuses a live `RimWorldLinux` pinned to the same profile, including one started outside the
launcher; a game with no readable `-savedatafolder` remains fail-closed.

`make sidecar-run` uses `~/.local/share/slopworld-car/profile`, whose parent is already exposed by
the debug preset. Its saves and settings are separate from the native profile; the daemon endpoint
is the only connection it inherits. A sidecar profile's first settings file selects the
`slopworld-warm` UI scheme; later launches leave that file alone.

## Refusing to patch outside the profile

`ModProfile.Ok` = is there a `slopworld.profile` marker in
`GenFilePaths.SaveDataFolderPath`. The launcher writes it, not the mod. Refusal
happens before anything is touched:

- `ModBootstrap` returns before `PatchAll`.
- `SteadyHands` returns before welding a `StatPart` onto a vanilla stat.
- `PatchOperationInProfile` wraps every XML op of ours that rewrites a vanilla def
  and answers true without running its `operations`; false would make the game log
  a failed patch.

Assemblies load before XML is patched, which is why that class works; static
constructors run after, which is why the C# gate is separate. Defs we *add*
survive a refusal, so `MainButtonWorker_Slop` gates all four buttons and shows the
dialog.
