# Build from source

Build and install SlopWorld from a checkout and run development checks. For prebuilt packages, see [Installation](../installation/linux.md).

## Prerequisites

- **Rust**: use rustup with the version and components pinned in `rust-toolchain.toml`.
- **.NET SDK**: install the version pinned in `global.json`. It builds the `net10.0`
  mod and runs C# tests and formatting. NuGet restores locked runtime packages.
- **Protobuf compiler (`protoc`)**: Rust builds require it. Contract generation
  requires **36.1** to keep generated C# output stable. Install the matching binary
  archive from the [Protobuf v36.1 release](https://github.com/protocolbuffers/protobuf/releases/tag/v36.1)
  and put its `bin` directory on `PATH`. Distro packages are suitable when they
  provide that version; check with `protoc --version`.
- **just 1.58 or newer** runs the repository recipes. Install it with your package manager
  or `cargo install just --locked`. On macOS, use `brew install just`.
- **Python 3.12 or newer** runs tooling and reads Cargo version metadata for mod builds.
  **uv** manages the tooling environment from `pyproject.toml` and `uv.lock`.
  Recipes synchronize locked dependencies automatically; `just tools-restore` prepares
  the environment explicitly. Asset generators use the optional `assets` dependencies
  and also require their native graphics libraries.

Set `RIMWORLD` to the RimWorld 1.7 installation directory containing
`Assembly-CSharp.dll`, `UnityStubs.dll`, and `Waylith.dll`. The default is
`~/.local/share/Steam/steamapps/common/RimWorld`. `MANAGED` can select a separate
assembly directory. For native macOS,
follow the [macOS guide](../installation/macos.md).

## Build and install

### Build

Run `just` to list commands, grouped by component. Recipes have flat names such as
`daemon-build` and `mod-test`. Use `just --show daemon-build` to inspect a recipe:

```sh
just build           # daemon, launcher, and mod
just daemon-build    # daemon and launcher
just mod-build       # mod; requires game assemblies
just sidecar-build   # native host-side container launcher; no game or protoc required
```

`BUILD` is `debug` by default. Use `just BUILD=release build` for a release build.
Settings accept environment values or `just NAME=value recipe` overrides. Put
assignments before recipe names. Tool executables come from `PATH`; repository
project filenames and formatter scope are fixed.

Root `build` and `install` recipes combine the daemon, launcher and mod.
`format`, `lint`, and `test` cover the daemon, sidecar, mod and tools;
regular tests also include sidecar, pager integration, and benchmark-helper tests.
Sidecar recipes use the `sidecar-` prefix; benchmark recipes use `bench-`. CI splits the same test suites across language jobs. Coverage collects daemon, mod and Python tool reports.

Run tasks separately when passing test filters: `just tools-test tools/recipes -k failure`.
Trailing arguments belong to that test runner. Chain root recipes with `just format lint`,
or component recipes such as `just daemon-lint mod-lint`.

Both modes produce `mod/Assemblies/SlopWorld.dll`; Git does not track this output.
`just mod-build` builds only C#; installation builds the Rust launcher when needed.
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
| `just lint` | Python Ruff and strict mypy checks, Rust formatting/Clippy, and C# formatting checks | — |
| `just test` | Daemon and sidecar Rust, C#, Python, pager, and benchmark-helper tests | `tmux`, `less`, Mono |
| `just ci` | Game-free tests with coverage, formatting, Rust lint, and generated-contract checks | `tmux`, `less`, coverage tools |

Use `daemon-test`, `mod-test`, `sidecar-test`, or `test-tools` to run a test subset.
`test-tools` runs Python, pager, and benchmark-helper checks shared with CI.
`just tools-test` runs tools, macOS, sidecar, and benchmark Python tests through pytest, without collecting coverage. Pass paths and pytest options
to select tests, for example `just tools-test tools/recipes -k failure`.
`just tools-test-pager` runs the pager integration checks.
Use `just tools-coverage` when you want a Python branch coverage report.
`just lint` checks sources without rewriting them. `just tools-lint` checks formatting,
Ruff rules and strict mypy types across `tools/`, including its tests.
`just mod-check-build` separately validates a Release build with warnings as errors
and requires game assemblies. Release packaging includes that check.
`just tools-format` applies the 120-column, single-quote style and sorts imports.
Ruff, mypy, and pytest-cov belong to the uv `dev` dependency group.
Tools run as modules from the repository root through uv;
for example, `uv run --locked python -m tools.docs.api_docs`.
Use `just deps-update-tools` after changing Python dependencies and commit `uv.lock`.
Use `just deps-update` to update dependency locks.
`deps-update` updates all Rust and Python dependencies within
their manifest constraints, re-resolves all NuGet lockfiles at their declared
package versions, and regenerates daemon and sidecar license records before
staging mod notices. Container images follow their configured tags, and system
packages installed inside containers follow their configured repositories.

Review and commit the resulting changes. After NuGet changes, run `just mod-restore`
and review the shipped runtime DLL changes under `mod/Assemblies`, including
checked-in transitive dependencies. Run `just mod-test` after runtime package
changes.
Run the relevant tests before releasing. Ordinary `just generate-all` regenerates
outputs from reviewed inputs and does not upgrade dependencies.
For an asset tool, use `uv run --locked --extra assets python -m tools.assets.emoji --help`.
`just mod-format` applies the pinned CSharpier formatter; `just mod-lint`
checks its output without game assemblies. Both restore local .NET tools automatically.

### Coverage

`just coverage` writes Python, Rust, and C# reports under `coverage/`. Install `cargo-llvm-cov` with
`cargo install cargo-llvm-cov --locked`. With rustup, install the matching LLVM tools using
`rustup component add llvm-tools-preview`; `cargo-llvm-cov` selects them from the active Rust toolchain.
Use `tools-coverage`, `daemon-coverage`, or `mod-coverage` for one component,
and `just coverage-summary` to summarize existing reports.
`daemon-coverage` clears workspace artifacts and profile data while retaining compiled dependencies.
CI retains instrumented dependencies in a cache isolated by compiler and coverage-tool settings.
For a system Rust installation, set `LLVM_COV` and `LLVM_PROFDATA` to matching tools.

## Local development loop

`just devloop` lists Git worktrees in the terminal where you run it.
Use these controls:

- Press Enter to rebuild the previous selection.
- Enter a number to select a different checkout.
- Enter `q` to exit.

The game starts only after the install step succeeds.
The picker also lists worktrees that you created manually.
It uses each checkout's normal host build directories.
The `sidecar-devloop` recipe uses a separate script.

## More workflows

### Benchmarks

`just BUILD=release bench-run gamefree --run <name>` measures the daemon, C#, and IPC
suites. Reports go to `bench/results/<name>/`. Use
`just bench-report --run <name>` to regenerate a report from saved CSVs;
`just bench-report --run <name> --baseline <older> --mode relative` compares runs.
Use `just BUILD=release bench-run daemon --run <name>` for one suite; only its
binaries are built. `bench-run mod` and `bench-run ipc` select the other suites.
Pass `--help` to a benchmark operation to see its CLI options. IPC benchmarks and
terminal-input HTTP regression require Mono.

### Related guides

- [IPC benchmark suite](https://github.com/droserasprout/slopworld/blob/main/bench/ipc/README.md) explains its scope.
- [Runtime package maintenance](https://github.com/droserasprout/slopworld/blob/main/mod/Dependencies/README.md) covers dependency updates.
- [Troubleshooting](../resources/troubleshooting.md) covers logs and diagnosis.
- [Contributing](contributing.md) covers repository and documentation workflows.

- [Packaging and releases](releases.md) covers native packages, archives, and publication.

<a id="rolling-github-release"></a>
<a id="requirements"></a>
<a id="build-packages"></a>
<a id="package-contents-and-versions"></a>
<a id="publish"></a>
<a id="debian-packages"></a>

See [Packaging and releases](releases.md) for archives, publication, and local
[Debian/Ubuntu](releases.md#debian-packages) or
[Fedora](releases.md#fedora-packages) builds.
