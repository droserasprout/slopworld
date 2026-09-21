# Build from source

## Toolchain

- **Rust** stable toolchain — builds the daemon and launcher.
- **.NET SDK** — builds the `net472` mod and runs C# tests and formatting.
  Framework reference assemblies are restored through NuGet; Mono is only needed
  to run the Mono IPC benchmark.
- **Protobuf compiler (`protoc`)** — Debian/Ubuntu: `protobuf-compiler`; Arch: `protobuf`; macOS: `brew install protobuf`. Required by Rust builds and `make api-contract`.
- **GNU Make** — all targets go through the Makefile. On macOS, install GNU Make with `brew install make` and use `gmake`.
- **PyYAML** — parses the compact shared wire contract used by `make api-contract`.

Set `RIMWORLD` to the Linux game directory (the folder containing `RimWorldLinux`). The mod links against assemblies in `Managed/`, so a real install is required.

## Targets

Run `make` for target help. Make owns dependencies and shared settings; scripts in
`tools/` own coverage, IPC benchmarks, and platform checks.
`mod/Source/SlopWorld/SlopWorld.csproj` owns C# compiler settings and references. Build with `make all`, or select `daemon` or
`mod`. Component checks also have `-daemon` and `-mod` targets.

`make install` installs the daemon, systemd unit, launcher, mod, and bundled UI
font. `make clean` removes build output.

## Build modes

`BUILD` is `debug` (default) or `release`. Both produce `mod/Assemblies/SlopWorld.dll`. `lint-mod` always rebuilds in Release.

SemVer-tagged builds embed the tag version. Untagged checkouts append the UTC build
date and short commit hash to the package version; trees without Git use the
package version alone.
The mod assembly is a local build output and is not checked into Git. The release workflow
publishes the daemon archive; source-based installs and Arch packages build the mod against the
target RimWorld installation before staging it.

The bundled `assets/fonts/clacon2.ttf` is installed to the current user's
`$XDG_DATA_HOME/fonts` directory by `make install` (`~/.local/share/fonts` by default).

## Formatting

C# formatting uses `dotnet format` in folder mode; `.editorconfig` preserves single-line statements. Override `DOTNET` to select the SDK command and `MANAGED` to select the game reference directory. `lint-mod` builds Release with warnings treated as errors.

Rust formatting and linting use `cargo fmt` and `cargo clippy`.

## Tests and coverage

`make test` runs Rust unit tests, game-free C# tests under `mod/Tests/`, and the prose linter's own tests.

`make coverage` produces Cobertura XML reports for both halves. It requires `cargo-llvm-cov` (install with `cargo install cargo-llvm-cov --locked`) and the matching `llvm-cov`/`llvm-profdata` binaries. Use `coverage-daemon` or `coverage-mod` to measure one half.

Each run prints a coverage summary and writes its fresh report to
`coverage/rust.cobertura.xml` or `coverage/csharp.cobertura.xml`. Client coverage measures
the production files linked into the test harness, not the entire game-bound mod.

`make bench` runs the daemon, C# and IPC benchmark suites serially; use `BUILD=release` for comparisons.

## Prose linter

`make lint-prose` scans Markdown files and source comments for LLM clichés. It exits nonzero on errors; density and vocabulary warnings are advisory unless `--fail-on-warnings` is passed.

Pass paths or CLI options through `PROSE_LINT_ARGS`; use
`make lint-prose PROSE_LINT_ARGS=--help` for the current options.

## Logs and diagnostics

The game log is Unity's `Player.log` — Harmony and mod exceptions land there, not in the terminal that launched the game. `make logs` tails it.

```sh
journalctl --user -u slopd -f               # daemon log
slopctl logs --follow                        # combined game + daemon
```

`make protobuf-deps` restores the locked Google.Protobuf runtime and its Mono dependencies.
`make bench-report` includes IPC timings, allocations, wire sizes and comparison ratios in its
three-run averaged report (`notes/perf-suite.md`), replacing the previous report.
Raw logs and CSVs stay local and ignored.
`make BUILD=release bench-ipc` compares the frozen JSON transport with Protobuf on Mono, .NET 8 and Rust;
see [the benchmark suite](../../bench/ipc/README.md) for scope and recorded results.

## Occasional maintenance

Run these scripts from the repository root, passing their options directly:

- `python3 tools/appicon.py`, `python3 tools/icons.py`, and `python3 tools/emoji_atlas.py` regenerate assets.
- `python3 tools/analyze_ui_schemes.py --check-warm` checks UI scheme luminance.
- `python3 tools/loc-report.py` writes a line-count snapshot.
- `bash tools/fetch-harmony.sh` updates Harmony.
- `bash tools/shot.sh OUTPUT` captures the game window (requires the `x11` sandbox preset).
