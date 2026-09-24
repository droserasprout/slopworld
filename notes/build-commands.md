# Build entry points

Use `make` (`gmake` on macOS). Its default target lists commands. The
[build guide](../docs/src/build.md) owns toolchains, tests, coverage and installation.

The common entry points live in `make/popular.mk`. Build, test/coverage, benchmark,
generation and lint recipes have separate owners in `make/`. `test-tools` owns shared
contract/catalog and maintenance-script checks.
Language test targets run only their own suite.

Make owns target dependencies and exports settings from `make/config.mk` to the
maintenance scripts in `tools/`. Keep multi-step shell logic there.

The shared C# formatter covers mod production, test, and IPC benchmark sources.
`make ci` runs this check without the game. The formatter excludes generated client bindings and build output.

The mod SDK project owns compiler settings, references and assembly metadata. Make
passes the configuration, game assembly path and daemon version. NuGet restores
locked .NET Framework reference assemblies.
Only IPC benchmarks need Mono.
Game references must keep `Private=false`: RimWorld loads every DLL in `Assemblies/`.

`test.yml` owns game-free checks for branch pushes, pull requests and manual runs.
`test-tools` checks plan status headers. `make install-git-hooks` configures the
local pre-commit guard against commits on `main`; it does not protect remote pushes.
Release CI calls it and packages the exact tested commit.
CI calls `make ci`, writes coverage rates to the job summary,
and uploads Cobertura reports as the `coverage` artifact. Supporting-tool and pager
tests run separately.
C# coverage measures only production files linked into the harness.

Release CI builds an existing tag (`0.1.0` or `v0.1.0`) and uploads daemon assets to
a draft release after game-free checks. Manual dispatch defaults to `0.1.0`.
Retagging occurs outside the workflow. Reruns replace matching daemon assets.
Mod builds and uploads remain local and manual. After retagging, replace any manually uploaded mod too.

The mod links against a real RimWorld install. Debug and release overwrite the same
`mod/Assemblies/SlopWorld.dll`.
`lint-mod` rebuilds Release. Do not infer the installed
assembly's build mode from its path.

`make validate-themes` checks every supplied UI and terminal TOML catalog file.
`mod`, `test-mod`, and `lint-mod` depend on it.

Mod installation stages and replaces only its destination through the Rust installer.
Adding a shipped top-level directory requires updating
`slopd/src/bin/slopworld/mod_install.rs`, not only build output.

The native daemon service prepends `~/.local/bin` to its inherited PATH so agents use the
CLI installed by `make install-daemon`. Existing agents retain their launch environment.
The installer compares both the running binary and installed unit before skipping restart.

`make bench-report` records three-run medians and between-run ranges, including Mono/CoreCLR/Rust IPC metrics.
Use `BENCH_REPORT_OUTPUT=notes/perf-suite-comparison.md` to retain an existing report during comparisons.
Raw runs remain local and ignored. Commit only processed reports supporting a concrete comparison.
`python3 tools/loc-report.py` creates a count snapshot on request. Keep reports only when they support a concrete comparison.
