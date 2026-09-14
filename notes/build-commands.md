# Build entry points

Use `make` (`gmake` on macOS); its default target lists commands. The
[build guide](../docs/src/build.md) owns toolchains, tests, coverage and installation.

The mod links against a real RimWorld install. Debug and release overwrite the same
`mod/Assemblies/SlopWorld.dll`; `lint-mod` rebuilds Release. Do not infer the installed
assembly's build mode from its path.

Mod installation stages and replaces only its destination through the Rust installer.
Adding a shipped top-level directory requires updating
`slopd/src/bin/slopworld/mod_install.rs`, not only build output.

`make bench-report` records three-run averages; `make loc-report` creates an on-demand
count snapshot. Keep reports only when they support a concrete comparison.
