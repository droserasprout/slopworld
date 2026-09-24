# Build from source

## Toolchain

- **Rust** stable toolchain — builds the daemon and launcher.
- **.NET SDK** — builds the `net472` mod and runs C# tests and formatting.
  NuGet restores framework reference assemblies.
  The IPC benchmarks and terminal-input HTTP regression require Mono.
- **Protobuf compiler (`protoc`)** — Rust builds and `make api-contract` require this compiler.
  Install `protobuf-compiler` on Debian/Ubuntu or `protobuf` on Arch.
  On macOS, run `brew install protobuf`.
- **GNU Make** — the Makefile defines all targets.
  On macOS, install GNU Make with `brew install make`.
  Use `gmake` on macOS.
- **PyYAML** — parses the compact shared wire contract used by `make api-contract`.

Set `RIMWORLD` to the Linux game directory (the folder containing `RimWorldLinux`).
Install RimWorld so the mod can link against assemblies in `Managed/`.

## Targets

Run `make` to list targets. Make defines dependencies and shared settings.
Scripts in `tools/` support maintenance, platform checks and the existing IPC suite.
Terminal-input benchmark code, tests, reports and local runs live under
`bench/terminal-input/`.
`mod/Source/SlopWorld/SlopWorld.csproj` owns C# compiler settings and references.
Build with `make all`, `make daemon`, or `make mod`.
Component checks also have `-daemon` and `-mod` targets.

`make install` installs the daemon, systemd unit, launcher, mod, and bundled UI
font. `make clean` removes build output.

## Build modes

`BUILD` is `debug` (default) or `release`.
Both produce `mod/Assemblies/SlopWorld.dll`.
`lint-mod` always rebuilds in Release.

SemVer-tagged builds embed the tag version.
Untagged checkouts append the UTC build date and short commit hash to the package version.
If a directory has no Git data, the build uses the package version alone.
The build produces the mod assembly locally. Git does not track it.
The release workflow publishes the daemon archive.
Source installations and Arch packages build the mod against the target RimWorld installation.

`make install` installs the supplied `assets/fonts/clacon2.ttf` in the current user's
`$XDG_DATA_HOME/fonts` directory (`~/.local/share/fonts` by default).

## Formatting

C# formatting uses `dotnet format` in folder mode for production code, tests, and
benchmark sources under `bench/`.
It excludes generated client bindings and build output.
`.editorconfig` preserves single-line statements.
`make check-format-csharp` checks this scope without game assemblies.
`make ci` runs this check.
`lint-mod` checks formatting after its Release build and treats warnings as errors.

Rust formatting and linting use `cargo fmt` and `cargo clippy`.

## Tests and coverage

`make test` runs all game-free tests: Rust, C# under `mod/Tests/`, supporting tools, and
pager integration.
Pager tests require `tmux` and `less`.
Use `test-daemon`, `test-mod`, `test-tools`, or `test-pager` to run a subset.

`make ci` runs Rust and C# tests with coverage.
It also runs supporting-tool and pager tests, Rust lint, and generated-contract drift checks.
GitHub Actions calls this target.
The target does not need game assemblies.
`make coverage-summary` summarizes existing reports.

`make coverage` produces Cobertura XML reports for Rust and C#.
It requires `cargo-llvm-cov` and the matching `llvm-cov`/`llvm-profdata` binaries.
Install `cargo-llvm-cov` with `cargo install cargo-llvm-cov --locked`.
Use `coverage-daemon` or `coverage-mod` to measure one component.

Each coverage run prints a summary.
The Rust report is `coverage/rust.cobertura.xml`.
The C# report is `coverage/csharp.cobertura.xml`.
C# coverage measures the production files linked into the test harness.

Rust also writes `coverage/rust.filtered.cobertura.xml` and a native per-file table at
`coverage/rust.files.txt`. Both use the same test run.
The default report follows `cargo-llvm-cov`'s built-in exclusions.
These include `tests.rs` and `*_tests.rs`.
The filtered report also applies `RUST_COVERAGE_EXCLUDE` from `make/config.mk`.
This filter omits generated bindings, standalone test files, benchmarks, and external Rust
library sources.
Override that variable to change the file scope.
Unit tests are in adjacent `*_tests.rs` or `tests.rs` files.
The source loads these files as child modules with `#[cfg(test)]` and `#[path]`.
The tests retain access to private implementation details.
Small test hooks embedded in production code remain measured by the file-based filter.
`make coverage-summary` prints both rates. File paths shared by multiple binaries may appear
more than once in LLVM reports.

`make bench` first builds all benchmark binaries.
Then it runs the daemon, C#, and IPC suites in sequence.
Use `BUILD=release` for comparisons.

## Logs and diagnostics

Tracing is opt-in. To collect performance and latency records while using
`devloop`, pass the flags in the process environment:

```sh
SLOPWORLD_DEBUG=1 SLOPWORLD_LATENCY=1 make devloop
```

The installer includes explicitly supplied tracing values in the service unit and
restarts the daemon when they change. A later installation without these values
restores the shipped unit. Capture and reporting instructions live in
`notes/terminal-latency.md` in the repository.

Unity writes Harmony and mod exceptions to `Player.log`, the game log.
These exceptions do not appear in the terminal that started the game.
`make logs` shows new entries in this file.

```sh
journalctl --user -u slopd -f               # daemon log
slopctl logs --follow                        # combined game + daemon
```

`make protobuf-deps` restores the locked Google.Protobuf runtime and its Mono dependencies.
`make bench-report` replaces `notes/perf-suite.md` with its latest report.
The report includes IPC timings, allocations, and wire sizes.
The command builds once before all three measurement runs.
Each timing shows the median p50 or p95 across runs.
Brackets show the minimum and maximum across runs.
These ranges show variation between runs. They are not confidence intervals.
Daemon measurements use calibrated batches of operations.
Raw logs and CSVs stay local and ignored.
`make BUILD=release bench-ipc` measures Protobuf on Mono, CoreCLR (.NET 8), and Rust.
See [the benchmark suite](../../bench/ipc/README.md) for scope and recorded results.

## Occasional maintenance

Run these scripts from the repository root.
Pass options directly to each script:

- `python3 tools/appicon.py`, `python3 tools/icons.py`, and `python3 tools/emoji_atlas.py`
  regenerate assets.
- `python3 tools/analyze_ui_schemes.py --check-warm` checks UI scheme luminance.
- `python3 tools/loc-report.py` writes a line-count snapshot.
- `bash tools/fetch-harmony.sh` updates Harmony.
- `bash tools/shot.sh OUTPUT` captures the game window (requires the `x11` sandbox preset).
