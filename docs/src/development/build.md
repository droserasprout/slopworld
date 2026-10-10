# Build from source

Build and install SlopWorld from a checkout and run development checks. For prebuilt packages, see [Installation](../installation/linux.md).

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
- **Python 3.12 or newer** runs tooling and reads Cargo version metadata for mod builds.
  **uv** manages the tooling environment from `pyproject.toml` and `uv.lock`.
  Recipes synchronize locked dependencies automatically; `just sync-tools` prepares
  the environment explicitly. Asset generators use the optional `assets` dependencies
  and also require their native graphics libraries.

Set `RIMWORLD` to the Linux game directory containing `RimWorldLinux`.
Building the mod requires the game's assemblies in `Managed/`. For native macOS,
follow the [macOS guide](../installation/macos.md).

## Build and install

### Build

Run `just` to list recipes by group. Use `just --groups` to list group names,
or `just --list --group Build` to show only build recipes:

```sh
just all       # daemon, launcher, and mod
just daemon    # daemon and launcher
just mod       # mod; requires game assemblies
just sidecar   # native host-side container launcher; no game or protoc required
```

`BUILD` is `debug` by default. Use `just BUILD=release all` for a release build.
Settings accept environment values or `just NAME=value recipe` overrides. Put
assignments before recipe names.

Both modes produce `mod/Assemblies/SlopWorld.dll`; Git does not track this output.
`just mod` builds only C#; installation builds the Rust launcher when needed.
`just clean` removes build output.

### Install and launch

Clone the repository, install, and launch:

```sh
git clone https://github.com/droserasprout/slopworld.git
cd slopworld
RIMWORLD=/path/to/RimWorld/game just install
slopworld
```

`just install` installs the daemon, systemd unit, launcher, and mod. A successful
Linux mod installation saves the game directory as the launcher default in
[game.toml](../reference/paths.md#launcher-configuration). If you move the game, run
`RIMWORLD=/new/path/to/game just install-mod`. Each successful installation replaces
the saved default; an invalid saved path reports an error. The launcher checks
`--game`, then `SLOPWORLD_GAME`, then the saved path, then `~/RimWorld/game` and
standard GOG and Steam paths. It uses a separate [game profile](../maintenance/game-profiles.md).

## Checks

### Format, lint, and test

| Target | Purpose | Extra requirements |
| --- | --- | --- |
| `just format` | Format Python tools and Rust/C# production, test, and benchmark sources | — |
| `just lint` | Python Ruff and strict mypy checks, Rust formatting/Clippy, and a Release mod build with C# formatting checks | Game assemblies |
| `just test` | All game-free Rust, C#, tool, and pager tests | `tmux`, `less` |
| `just ci` | Game-free tests with coverage, formatting, Rust lint, and generated-contract checks | `tmux`, `less`, coverage tools |

Use `test-daemon`, `test-sidecar`, `test-mod`, `test-tools`, or `test-pager` to run a subset.
`just test-tools` runs benchmark helper tests and the Python package tests through pytest,
without collecting coverage. Existing unittest tests run under pytest.
Use `just coverage-tools` when you want a Python branch coverage report.
`just lint-tools` formats the package and sorts imports before checking it with Ruff
and running strict mypy across `tools/`, including its tests.
`just format-tools` applies the 120-column, single-quote style and sorts imports.
Ruff, mypy, and pytest-cov belong to the uv `dev` dependency group.
Tools run as modules from the repository root through uv;
for example, `uv run --locked python -m tools.docs.api_docs`.
Use `just lock-tools` after changing Python dependencies and commit `uv.lock`.
For an asset tool, use `uv run --locked --extra assets python -m tools.assets.emoji --help`.
`just check-format-mod` checks C# formatting without game assemblies.

### Coverage

`just coverage` writes Python, Rust, and C# reports under `coverage/`. Install `cargo-llvm-cov` with
`cargo install cargo-llvm-cov --locked` and the matching `llvm-cov` and
`llvm-profdata` binaries. Use `coverage-tools`, `coverage-daemon`, or `coverage-mod` for one component,
and `just coverage-summary` to summarize existing reports.

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

## More workflows

### Benchmarks

`just BUILD=release BENCH_RUN=<name> bench` measures the daemon, C#, and IPC
suites. Reports go to `bench/results/<name>/`. Use
`just BENCH_RUN=<name> bench-report` to regenerate a report from saved CSVs;
`BENCH_BASELINE=<older> BENCH_MODE=relative` compares runs. IPC benchmarks and
terminal-input HTTP regression require Mono.

### Related guides

- [IPC benchmark suite](https://github.com/droserasprout/slopworld/blob/main/bench/ipc/README.md) explains its scope.
- [Runtime package maintenance](https://github.com/droserasprout/slopworld/blob/main/mod/Dependencies/README.md) covers dependency updates.
- [Troubleshooting](../help/troubleshooting.md) covers logs and diagnosis.
- [Contributing](contributing.md) covers repository and documentation workflows.

- [Packaging and releases](releases.md) covers archives, Debian packages, and publication.

<a id="rolling-github-release"></a>
<a id="requirements"></a>
<a id="build-packages"></a>
<a id="package-contents-and-versions"></a>
<a id="publish"></a>
<a id="debian-packages"></a>

For release archives and publication, see
[Packaging and releases](releases.md). For a Debian or Ubuntu build,
use [Debian packages](releases.md#debian-packages).
