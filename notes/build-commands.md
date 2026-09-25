# Build entry points

Use `make` (`gmake` on macOS). Its default target lists commands. The
[build guide](../docs/src/build.md) owns toolchains, tests, coverage and installation.

The common entry points live in `make/popular.mk`. Build, test/coverage, benchmark,
generation and lint recipes have separate owners in `make/`. `test-tools` owns shared
contract/catalog and maintenance-script checks.
Language test targets run only their own suite.

Make owns target dependencies and exports settings from `make/config.mk` to the
maintenance scripts in `tools/`. Keep multi-step shell logic there.

The shared C# formatter covers mod production and test sources plus C# benchmark
sources under `bench/`.
`make ci` runs this check without the game. The formatter excludes generated client bindings and build output.

The mod SDK project owns compiler settings, references and assembly metadata. Make
passes the configuration, game assembly path and daemon version. NuGet restores
locked .NET Framework reference assemblies.
IPC benchmarks and the terminal-input HTTP regression need Mono.
Game references must keep `Private=false`: RimWorld loads every DLL in `Assemblies/`.

`test.yml` owns game-free checks for branch pushes, pull requests and manual runs.
`test-tools` checks plan status headers. `make install-git-hooks` configures the
local pre-commit guard against commits on `main`. It does not protect remote pushes.
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
The installer compares both the running binary and effective installed unit before skipping restart.
Tracing stays opt-in: prefix `make devloop` with `SLOPWORLD_DEBUG=1`
for a capture. The installer materializes explicit values in the service unit because
systemd does not inherit the installing shell environment. Later installs without
overrides restore the shipped unit.

`make BUILD=release bench BENCH_RUN=<name>` builds once and records three runs of
daemon, C#, and IPC metrics. Focused `bench-daemon`, `bench-mod`, and `bench-ipc`
targets record one run. `make bench-terminal BENCH_RUN=<name> BENCH_PHASE=typing`
records the interactive terminal workload on the graphical host. Reuse a run name
to combine game-free and terminal phases; each phase refuses to overwrite its own
measurements. Each writes CSV measurements, outcomes, and metadata under ignored
`bench/results/<name>/`, with auditable logs and traces under `raw/`. History
setup uses that run's `tmp/`.
`make bench-terminal-typing` fills an empty local host-shell tab and measures
only typing, with repaint reasons in its saved performance summary.
Daemon storage probes and IPC fixture encoding use a per-repetition `tmp/` under
the same results filesystem, removed after success. Compare runs on the same
filesystem when judging storage lanes.

`make bench-report BENCH_RUN=<name>` reads the CSVs without running benchmarks.
Add `BENCH_BASELINE=<older> BENCH_MODE=relative` for percentage changes, or use
the default absolute medians with sample standard deviation when repeated.
`BENCH_REPORT_OUTPUT=<path>` exports a
Markdown report elsewhere. Game-free comparisons require matching recorded build
and host details; a missing or different environment withholds the percentage.
Terminal comparisons match backend, input rate, speed multiplier, and viewport
context. History also requires the same observed scroll input sources. The session inventory count stays in raw CSVs but is omitted from reports
and ignored when pairing otherwise identical active-terminal measurements. Keep
background load comparable.
The committable report is a single stable snapshot under `bench/`; local runs
and their CSVs remain ignored.
For a focused run, `make bench-latest BENCH_RUN=<focused> BENCH_FALLBACK_RUN=<full>`
fills absent suites and phases from the saved full run. The report labels each
source without dated run names; a phase is never mixed across runs.
For an empty-shell history run, pass `BENCH_PHASE=history BENCH_FILL_HISTORY=1`.
`python3 tools/loc-report.py` creates a count snapshot on request. Keep reports only when they support a concrete comparison.

The terminal-input runner and helpers live under `../bench/terminal-input/`.
Its default results path is also `bench/results/<UTC timestamp>/`.
See [terminal latency tracing](terminal-latency.md) for the workflow.

History preparation checks the pane limit against `SCROLLBACK_LINES`, then emits
that many rows plus the viewport and settles outside the measured interval. tmux
can trim history in chunks. Its current retained-row count is not the emulator's
history capacity. The fixture targets capacity by emitted lines, not by polling
Unity's view. Its private completion file requires a local host shell.
