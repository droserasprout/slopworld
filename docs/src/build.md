# Build from source

## Prerequisites

- **Rust** stable toolchain builds the daemon and launcher using the 2024 edition.
- **.NET SDK**: install the version pinned in `global.json`. It builds the `net472`
  mod and runs C# tests and formatting. NuGet restores framework reference assemblies.
- **Protobuf compiler (`protoc`)**: Rust builds require it. Contract generation
  requires **36.1** to keep generated C# output stable. Install the matching binary
  archive from the [Protobuf v36.1 release](https://github.com/protocolbuffers/protobuf/releases/tag/v36.1)
  and put its `bin` directory on `PATH`. Distro packages are suitable when they
  provide that version; check with `protoc --version`.
- **just** runs the repository recipes. Install it with your package manager
  or `cargo install just --locked`. On macOS, use `brew install just`.
- **Python 3.11 or newer** runs tooling and reads Cargo version metadata for mod builds.
  **uv** manages the tooling environment from `pyproject.toml` and `uv.lock`.
  Recipes synchronize locked dependencies automatically; `just sync-tools` prepares
  the environment explicitly. Asset generators use the optional `assets` dependencies
  and also require their native graphics libraries.

Set `RIMWORLD` to the Linux game directory containing `RimWorldLinux`.
Building the mod requires the game's assemblies in `Managed/`. For native macOS,
follow the [macOS guide](guides/macos.md).

## Build and install

Run `just` to list recipes by group. Use `just --groups` to list group names,
or `just --list --group Build` to show only build recipes:

```sh
just all       # daemon, launcher, and mod
just daemon    # daemon and launcher
just mod       # mod; requires game assemblies
```

`BUILD` is `debug` by default. Use `just BUILD=release all` for a release build.
Settings accept environment values or `just NAME=value recipe` overrides. Put
assignments before recipe names.

Both modes produce `mod/Assemblies/SlopWorld.dll`; Git does not track this output.
`just mod` builds only C#; installation builds the Rust launcher when needed.
`just clean` removes build output. Exact numeric `MAJOR.MINOR.PATCH` tags at HEAD, optionally prefixed by `v`, set
the release version. Untagged checkouts append the UTC build date and short hash
to the package version. Without Git data, builds use the package version alone.

`just install` installs the daemon, systemd unit, launcher, and mod.
See [Install](install.md) for the complete setup procedure.

## Checks

| Target | Purpose | Extra requirements |
| --- | --- | --- |
| `just format` | Format Python tools and Rust/C# production, test, and benchmark sources | — |
| `just lint` | Python Ruff checks, Rust formatting/Clippy, and a Release mod build with C# formatting checks | Game assemblies |
| `just test` | All game-free Rust, C#, tool, and pager tests | `tmux`, `less` |
| `just ci` | Game-free tests with coverage, formatting, Rust lint, and generated-contract checks | `tmux`, `less`, coverage tools |

Use `test-daemon`, `test-mod`, `test-tools`, or `test-pager` to run a subset.
`just test-tools` runs benchmark helper tests and the Python package tests through pytest,
writing branch coverage to `coverage/python.cobertura.xml`. Existing unittest tests run under pytest.
`just lint-tools` formats the package and sorts imports before checking it with Ruff.
`just format-tools` applies the 120-column, single-quote style and sorts imports. Both tools and pytest-cov belong
to the uv `dev` dependency group. Tools run as modules from the repository root through uv;
for example, `uv run --locked python -m tools.docs.reference`.
Use `just lock-tools` after changing Python dependencies and commit `uv.lock`.
For an asset tool, use `uv run --locked --extra assets python -m tools.assets.emoji --help`.
`just check-format-csharp` checks C# formatting without game assemblies.

`just coverage` writes Python, Rust, and C# reports under `coverage/`. Install `cargo-llvm-cov` with
`cargo install cargo-llvm-cov --locked` and the matching `llvm-cov` and
`llvm-profdata` binaries. Use `coverage-tools`, `coverage-daemon`, or `coverage-mod` for one component,
and `just coverage-summary` to summarize existing reports.

## More workflows

`just BUILD=release BENCH_RUN=<name> bench` measures the daemon, C#, and IPC
suites. Reports go to `bench/results/<name>/`. Use
`just BENCH_RUN=<name> bench-report` to regenerate a report from saved CSVs;
`BENCH_BASELINE=<older> BENCH_MODE=relative` compares runs. IPC benchmarks and
terminal-input HTTP regression require Mono.

- [Terminal latency measurements](guides/terminal-latency.md) covers desktop
  input benchmarks, tracing, and report interpretation.
- [IPC benchmark suite](../../bench/ipc/README.md) explains its scope.
- [Runtime package maintenance](../../mod/Dependencies/README.md) covers dependency updates.
- [Troubleshooting](reference/troubleshooting.md) covers logs and diagnosis.
- [Contributing](reference/contributing.md) covers repository and documentation workflows.

## Local development loop

`just devloop` lists Git worktrees in the terminal where you run it.
Use these controls:

- Press Enter to rebuild the previous selection.
- Enter a number to select a different checkout.
- Enter `q` to exit.

The game starts only after the install step succeeds.
The picker also lists worktrees that you created manually.
It uses each checkout's normal host build directories.
The sidecar devloop uses a separate script.
