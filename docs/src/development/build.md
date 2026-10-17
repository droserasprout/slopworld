# Build from source

Build and install SlopWorld on native Linux, then run checks for your changes.
For prebuilt packages, see [Linux installation](../installation/linux.md).
For native macOS or a container deployment, follow the
[macOS](../installation/macos.md) or [Sidecar mode](../installation/sidecar.md) guide.

## Prerequisites

Install these tools before building:

| Tool | Requirement |
| --- | --- |
| Rust | Use rustup with the version and components pinned in `rust-toolchain.toml`. |
| .NET SDK | Install the version pinned in `global.json`. NuGet supplies the framework reference assemblies for the `net472` mod. |
| Protobuf compiler (`protoc`) | Required for Rust builds. Use **36.1** for stable generated C# output; check with `protoc --version`. |
| just | Version **1.58 or newer**. Install through your package manager or with `cargo install just --locked`. |
| Python and uv | Python **3.12 or newer** and uv. Recipes synchronize the locked tooling dependencies automatically. |

If your distribution does not supply protoc 36.1, download the matching binary
archive from the [Protobuf v36.1 release](https://github.com/protocolbuffers/protobuf/releases/tag/v36.1)
and put its `bin` directory on `PATH`.

You also need RimWorld **1.6**. Set `RIMWORLD` to the directory containing
`RimWorldLinux`; the mod build reads the game's assemblies from
`RimWorldLinux_Data/Managed/`.

## Build and install

### Install and launch

Clone the repository and run the installer, replacing the game path below:

```sh
git clone https://github.com/droserasprout/slopworld.git
cd slopworld
RIMWORLD=/path/to/RimWorld/game just install
slopworld
```

`just install` builds and installs the daemon, CLI, launcher, systemd user service,
and mod. Launch with `slopworld` to use the separate
[game profile](../maintenance/game-profiles.md).

Installation saves the Linux game directory in
[game.toml](../reference/paths.md#launcher-configuration). If you move the game,
run `RIMWORLD=/new/path/to/game just install-mod` to update the mod and saved path.

### Build

To compile without installing, choose a target:

| Command | Builds |
| --- | --- |
| `just build` | Daemon, CLI, launcher, and mod |
| `just daemon-build` | Daemon, CLI, and launcher |
| `just mod-build` | C# mod; requires game assemblies |
| `just sidecar-build` | Host-side container launcher; requires neither the game nor protoc |

Builds use Debug mode by default. Use `just BUILD=release build` for Release.
Both modes write the mod to `mod/Assemblies/SlopWorld.dll`, which Git ignores.
Run `just clean` to remove build and coverage output.

Run `just` to list recipes or `just --show daemon-build` to inspect one.
Settings accept environment values or overrides such as
`just RIMWORLD=/path/to/game BUILD=release build`. Put assignments before recipe names.

## Checks

### Format, lint, and test

Run the relevant tests before finishing a change. These recipes cover the daemon,
sidecar, mod, and Python tools:

| Command | Purpose |
| --- | --- |
| `just format` | Apply Rust, C#, and Python formatting. |
| `just lint` | Check formatting, Rust Clippy warnings, Python Ruff rules, and strict mypy types. |
| `just test` | Run game-free tests, including pager integration and benchmark helpers. Requires `tmux`, `less`, and Mono. |
| `just ci` | Run the game-free CI checks, including coverage and generated-protocol validation. Also requires the [coverage tools](#coverage). |

To check C# compilation against the game assemblies, run `just mod-check-build`.
It builds Release with warnings treated as errors and is also part of release packaging.
Game-free C# tests and formatting do not require those assemblies.

For documentation changes, run `just docs-build`. See
[Contributing](contributing.md#documentation) for book validation and preview commands.

### Check one component

| Scope | Format | Lint | Test |
| --- | --- | --- | --- |
| Daemon | `daemon-format` | `daemon-lint` | `daemon-test` |
| Sidecar | `sidecar-format` | `sidecar-lint` | `sidecar-test` |
| Mod | `mod-format` | `mod-lint` | `mod-test` |
| Python tools | `tools-format` | `tools-lint` | `tools-test` |

Prefix each recipe with `just`. C# formatting uses the pinned CSharpier tool,
restored automatically. Python formatting uses 120 columns, single quotes, and sorted imports.

`just test-tools` also checks theme catalogs and pager integration.
Use `just tools-test-pager` for pager checks alone.

Pass test filters to a component recipe. For example:

```sh
just tools-test tools/recipes -k failure
```

Run filtered tests separately: trailing arguments belong to that test runner.
Recipes without trailing arguments can be combined, as in `just format lint`
or `just daemon-lint mod-lint`.

### Coverage

For Rust coverage, install `cargo-llvm-cov`:

```sh
cargo install cargo-llvm-cov --locked
rustup component add llvm-tools-preview
```

`cargo-llvm-cov` selects matching LLVM tools from the active rustup toolchain.
With a system Rust installation, set `LLVM_COV` and `LLVM_PROFDATA` to matching tools.

Run `just coverage` to write Python, Rust, and C# reports under `coverage/`.
Use `tools-coverage`, `daemon-coverage`, or `mod-coverage` for one component,
and `just coverage-summary` to summarize saved reports.
Daemon coverage clears workspace artifacts and profile data while retaining compiled dependencies.

## Local development loop

Run `just devloop` to select a Git worktree, reinstall, and launch the game.
After each game exit:

- Press Enter to rebuild the previous selection.
- Enter a number to select a different checkout.
- Enter `q` to exit.

The picker includes manually created worktrees and uses each checkout's normal
host build directories. The game starts only after installation succeeds.
For a sidecar deployment, use `just sidecar-devloop`.

<a id="dependency-maintenance"></a>
<a id="benchmarks"></a>
<a id="more-workflows"></a>

## Related guides

- [Developer build notes](https://github.com/droserasprout/slopworld/blob/main/notes/build-commands.md) cover dependency maintenance and benchmarks.
- [Contributing](contributing.md) covers repository and documentation workflows.
- [Troubleshooting](../resources/troubleshooting.md) covers logs and diagnosis.
- [Packaging and releases](releases.md) covers archives and publication.

<a id="rolling-github-release"></a>
<a id="requirements"></a>
<a id="build-packages"></a>
<a id="package-contents-and-versions"></a>
<a id="publish"></a>
<a id="debian-packages"></a>

For distribution packages, follow the
[Arch](releases.md#arch-packages), [Debian/Ubuntu](releases.md#debian-packages),
or [Fedora](releases.md#fedora-packages) instructions.
