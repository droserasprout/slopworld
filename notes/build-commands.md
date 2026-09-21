# Build entry points

Use `make` (`gmake` on macOS); its default target lists commands. The
[build guide](../docs/src/build.md) owns toolchains, tests, coverage and installation.

Make owns target dependencies and exports settings from `make/config.mk` to the
maintenance scripts in `tools/`. Keep multi-step shell logic there.

The mod SDK project owns compiler settings, references and assembly metadata. Make
passes the configuration, game assembly path and daemon version. NuGet restores
locked .NET Framework reference assemblies; Mono is only needed for IPC benchmarks.
Game references must keep `Private=false`: RimWorld loads every DLL in `Assemblies/`.

The mod links against a real RimWorld install. Debug and release overwrite the same
`mod/Assemblies/SlopWorld.dll`; `lint-mod` rebuilds Release. Do not infer the installed
assembly's build mode from its path.

`make validate-themes` checks every shipped UI and terminal TOML catalog file; `mod`,
`test-mod`, and `lint-mod` depend on it.

Mod installation stages and replaces only its destination through the Rust installer.
Adding a shipped top-level directory requires updating
`slopd/src/bin/slopworld/mod_install.rs`, not only build output.

The native daemon service prepends `~/.local/bin` to its inherited PATH so agents use the
CLI installed by `make install-daemon`. Existing agents retain their launch environment.
The installer compares both the running binary and installed unit before skipping restart.

`make bench-report` records three-run medians and between-run ranges including Mono/.NET/Rust IPC metrics and keeps
raw runs ignored locally; only the current processed report is committed; `python3 tools/loc-report.py` creates an on-demand
count snapshot. Keep reports only when they support a concrete comparison.
