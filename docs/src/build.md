# Build from source

## Toolchain

- **Rust** stable toolchain — builds the daemon and launcher.
- **Mono** (`csc`) — compiles the mod. The .NET SDK is optional and used only for `dotnet format`.
- **GNU Make** — all targets go through the Makefile. On macOS, install GNU Make with `brew install make` and use `gmake`.

Set `RIMWORLD` to the Linux game directory (the folder containing `RimWorldLinux`). The mod links against assemblies in `Managed/`, so a real install is required.

## Targets

`make` with no arguments prints the full target list. The important ones:

| Target | What it does |
| --- | --- |
| `all` | Builds both the daemon and the mod. |
| `daemon` | `cargo build` in `slopd/`. Pass `BUILD=release` for a release build. |
| `mod` | Compiles the mod with Mono `csc` into `mod/Assemblies/SlopWorld.dll`. |
| `test` | Runs `cargo test`, the game-free C# tests, and the prose linter tests. |
| `format` | Formats both halves. `-daemon` and `-mod` variants exist. |
| `lint` | Lints both halves. `-daemon` and `-mod` variants exist. |
| `install` | Installs the daemon binary, systemd unit, runner, and mod using the tested Rust mod installer. |
| `clean` | Removes build output. |

## Build modes

`BUILD` is `debug` (default) or `release`. Both produce `mod/Assemblies/SlopWorld.dll`. `lint-mod` always rebuilds in Release.

The first release uses the canonical `v0.0.1` tag and embeds `0.0.1`. Untagged checkouts append
the UTC build date and short hash to the package version: `0.0.1-20260831-3eb9902`. Trees
without Git keep the `0.0.1` fallback.
The release workflow packages the checked-in `mod/Assemblies/SlopWorld.dll`; because that
assembly is compiled against a local RimWorld install, rebuild it with the target version before
creating a release tag.

## Formatting

C# formatting uses `dotnet format` in folder mode; `.editorconfig` preserves single-line statements. Override `CSC` or `CSC_API` when the compiler or Mono reference assemblies are elsewhere. `format-mod` requires the .NET SDK; `lint-mod` does not.

Rust formatting and linting use `cargo fmt` and `cargo clippy`.

## Tests and coverage

`make test` runs Rust unit tests, game-free C# tests under `mod/Tests/`, and the prose linter's own tests.

`make coverage` produces Cobertura XML reports for both halves. It requires `cargo-llvm-cov` (install with `cargo install cargo-llvm-cov --locked`) and the matching `llvm-cov`/`llvm-profdata` binaries. Use `coverage-daemon` or `coverage-mod` to measure one half.

## Prose linter

`make lint-prose` scans Markdown files and source comments for LLM clichés. It exits nonzero on errors; density and vocabulary warnings are advisory unless `--fail-on-warnings` is passed.

```sh
make lint-prose                              # scan the whole repo
make PROSE_LINT_ARGS="docs/" lint-prose       # scan only docs/
python3 tools/prose_lint.py --list-rules     # show all rules
python3 tools/prose_lint.py --rule ai-vocab   # check one rule
```

## Logs and diagnostics

The game log is Unity's `Player.log` — Harmony and mod exceptions land there, not in the terminal that launched the game. `make logs` tails it.

```sh
journalctl --user -u slopd -f               # daemon log
slopctl logs --follow                        # combined game + daemon
```
