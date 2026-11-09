# Build from source

Build and install SlopWorld from a source checkout, then run checks for your changes.
For prebuilt packages, see [Linux installation](../installation/linux.md).
To build and run the daemon in a container, follow the
[Sidecar mode](../installation/sidecar.md) guide.

## Prerequisites

Install these tools before building:

| Tool | Requirement |
| --- | --- |
| Rust | Use rustup with the version and components pinned in `rust-toolchain.toml`. |
| Native compiler toolchain | Install a C compiler (GCC or Clang), libc development headers, and build tools such as `make`. On Debian/Ubuntu, install `build-essential`. |
| LLVM linker (`lld`) | Required by the daemon crate's Cargo configuration on x86_64 Linux. |
| pkg-config, ALSA and D-Bus development files | Required for the Linux daemon's audio backend and real-time thread promotion. Debian/Ubuntu: `pkg-config`, `libasound2-dev` and `libdbus-1-dev`; Fedora: `pkgconf-pkg-config`, `alsa-lib-devel` and `dbus-devel`; Arch: `pkgconf`, `alsa-lib` and `dbus`. |
| .NET SDK | Install the version pinned in `global.json`. It builds the `net10.0` mod. NuGet restores locked runtime packages. |
| Protobuf compiler (`protoc`) | Required for Rust builds. Use **36.1** for stable generated C# output; check with `protoc --version`. |
| just | Version **1.58 or newer**. Install through your package manager or with `cargo install just --locked`. |
| Python and uv | Python **3.12 or newer** and uv. Recipes synchronize the locked tooling dependencies automatically. |
| tmux | **3.7 or newer** for runtime sessions and terminal tests; CI and the sidecar pin 3.7c. |
| Fontconfig | Linux terminal font fallback uses `libfontconfig.so.1`; C# integration tests also require `fc-match` and an installed font. Python symbol-atlas tests require `fc-query`. |

If your distribution does not supply protoc 36.1, download the matching binary
archive from the [Protobuf v36.1 release](https://github.com/protocolbuffers/protobuf/releases/tag/v36.1)
and put its `bin` directory on `PATH`.

You also need RimWorld **1.7**. Set `RIMWORLD` to the installation directory
containing `Assembly-CSharp.dll`, `UnityStubs.dll`, and `Waylith.dll`. The default is
`~/.local/share/Steam/steamapps/common/RimWorld`. `MANAGED` can select a separate
assembly directory.

Documentation builds and release validation also require the pinned
[mdBook setup](contributing.md#documentation).

## Build and install

### Install and launch

Install the [runtime dependencies](../installation/linux.md#requirements) as well
as the compiler tools above; the source installer does not install host packages.
Clone the repository and check for missing runtime tools before installing,
replacing the game path below:

```sh
git clone https://github.com/droserasprout/slopworld.git
cd slopworld
just tools-check-requirements
RIMWORLD=/path/to/RimWorld/game just install
slopworld
```

`just install` builds and installs the daemon, CLI, launcher, systemd user service,
and mod. Launch with `slopworld` to use the separate
[game profile](../maintenance/game-profiles.md).

Installation saves the game directory in
[game.toml](../reference/paths.md#launcher-configuration). If you move the game,
run `RIMWORLD=/new/path/to/game just install-mod` to update the mod and saved path.

### Build

To compile without installing, choose a target:

| Command | Builds |
| --- | --- |
| `just build` | Daemon, CLI, launcher, and mod |
| `just daemon-build` | Daemon, CLI, and launcher |
| `just mod-build` | C# mod against installed game assemblies |
| `just mod-build rimref` | C# mod against pinned RimRef 1.7.5000-unstable references; no game installation required |
| `just sidecar-build` | Host-side container launcher; requires neither the game nor protoc |
| `just package-sidecar debian` | Independent slopcar package on the selected native distribution (`arch`, `debian`, or `fedora`) |
| `just release-sidecar amd64` | Tagged sidecar distro packages for `amd64` or `arm64`; requires containers and matching architecture or emulation |

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
| `just test` | Run game-free tests, including pager integration and benchmark helpers. Requires `tmux` and `less`. |
| `just ci` | Run the game-free CI checks, including coverage and generated-protocol validation. Also requires the [coverage tools](#coverage). |

To check C# compilation against the game assemblies, run `just mod-check-build`.
It builds Release with warnings treated as errors, resolves native text patch targets,
and checks the swapchain rewrite against installed game assemblies without starting
the game. Local release packaging includes this check; hosted packaging compiles
against pinned RimRef references without inspecting game IL.
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

- Press Enter or wait 10 seconds to rebuild the previous selection.
- Enter a number to select a different checkout.
- Enter `q` to exit.

The picker includes manually created worktrees and uses each checkout's normal
host build directories. The game starts only after installation succeeds.
For a sidecar deployment, use `just sidecar-devloop`.

<a id="dependency-maintenance"></a>
<a id="benchmarks"></a>
<a id="more-workflows"></a>

## Related guides

- [Developer build notes](https://github.com/droserasprout/slopworld/blob/main/notes/build-commands.md) describe build ownership, dependency locks, and test boundaries.
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
