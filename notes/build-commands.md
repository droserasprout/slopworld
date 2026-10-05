# Build entry points

Use `just` to list recipes. [Build from source](../docs/src/build.md)
owns toolchains, command usage, tests, coverage, and benchmark workflow.
Common entry points live in `just/popular.just`; imported `.just` files own recipes,
and `just/config.just` owns shared settings and coverage scope.
`mac/` owns native macOS checks, workflow modules, settings, and a separate justfile. It runs
from the repository root and calls the root justfile explicitly for shared builds;
exported settings carry across that boundary. Python modules own multi-step
workflow orchestration rather than duplicating it in recipes. `mac/setup.sh` bootstraps
uv before Python recipes can run. Settings accept environment values
or `just NAME=value recipe`; assignments must precede recipe names. Exported settings
carry into recursive calls. `SLOPCAR_PROFILE` stays unexported so native commands do
not select the sidecar profile; sidecar launch recipes set it explicitly.

Mod compilation does not build Rust binaries. `tools/version/mod_version.py` reads the
Cargo package fallback and uses `tools/version/resolve.py` for the same tag/date/commit
rules as Rust; `VERSION` overrides either build. Installer and launch recipes
own their launcher build dependency. Protocol generators publish only changed
bytes so repeated recipe invocations preserve compiler input timestamps.
`devloop` runs `just install run` together, sharing build dependencies once per
iteration and retaining each worktree's Rust target and C# obj directories.
Only an explicit `just clean` removes build output.

`just ci` checks generated files, formatting and tool/pager behavior before lint
and coverage. The test workflow exposes those stages separately for timings.
CI installs pinned tools, including uv, through
`.github/actions/setup-build-tools/`. It owns archive caching, tool versions,
and the protoc checksum for generated bindings.
Only superseded branch-push test runs are cancelled; called tests and
manual runs have isolated concurrency groups. NuGet caching includes the locked
runtime/test dependency graphs and the coverage tool manifest.

`packaging/arch/` owns Arch package staging and user setup hooks. The local package
builds a snapshot without private files or build caches; preparation records the
binary version for package metadata, build and check. Cargo and uv fetch locked dependencies
in preparation; later builds use frozen Cargo dependencies and offline uv execution.
Arch runtime requirements cover the audio library, default shell, core session
infrastructure, Git/worktrees and workspace search. Pager/editor/highlighter tools,
fallback process inspection and desktop/music integrations are optional dependencies.
Pager and highlighter defaults use Auto to select installed tools. The editor still
defaults to micro; optional package metadata does not imply an editor fallback.

`tools/` is a repository Python package, with focused subpackages and tests beside
their owners. `tools.ROOT` owns checkout paths; Python recipes use `uv run --locked python -m`
and package imports rather than adding script directories to `sys.path`.
`pyproject.toml` declares Python dependencies and `uv.lock` pins them. uv maintains
the ignored `.venv/` without installing the repository package. Asset generators
opt into the `assets` extra. The uv `dev` group owns Ruff, pytest, and pytest-cov.
`just lint-tools` formats sources and sorts imports before checking core correctness rules; `just test-tools`
prepares shared inputs and runs package tests with branch coverage, excluding test
files and package markers. `just lock-tools` updates dependency resolution.

`tools/utils.py` owns shared command parsing, subprocess execution, logging, and CLI
failure handling. Command settings use shell-style argument quoting without executing
a shell. `tools/host/` owns daemon installation, GOG setup, and worktree development loops.
`tools/coverage/collect.py` owns Rust and C# coverage orchestration.

The daemon installer compares running executable contents and the managed unit
fragment; systemd drop-ins are outside that comparison. Inactive units activate once
through restart. GOG login captures the CLI token response and verifies success before
accepting saved credentials.

`bench/runner.py` owns benchmark build/run subprocesses; `bench/report.py` owns
collection and reporting. `bench/` also owns shared result handling and its tests.
`tools/analysis/loc_report.py` writes working-tree snapshots under ignored `dist/`
by default, marks tracked modifications, and refuses to overwrite reports.
`slopcar/` owns the shared container devloop; platform workflows call it through `just`.
`tools/assets/` owns asset generators and text-sprite checks; `assets/` owns bundled
source data and icon manifests. Generated runtime assets stay in `mod/`.
OST production scripts and staged audio belong to the private repository under
`priv/ost/`; shipped soundtrack files and song definitions belong to `mod/`.

The [attribution policy](core-attribution.md) applies to used libraries and assets.
`licenses/` owns canonical third-party texts and attribution; the root `LICENSE`
owns SlopWorld terms. `just stage-licenses` stages ignored copies into
`mod/About/ThirdPartyNotices/` and `mod/About/LICENSE`; `just check-licenses`
verifies their contents and rejects stale extra files. The mod build stages them
automatically. Mod installers and Arch packages copy `About/` recursively.
Release archives, Arch packages, and sidecar images also include readable copies
from the canonical sources. Update only the canonical files.
`just rust-licenses` refreshes the Rust inventory from the locked Cargo graph,
including build/dev dependencies and all target platforms. Cargo manifests own
its license metadata; the generated table does not replace distribution notices.

The mod project owns game references/compiler settings; `Directory.Build.props`
and `.editorconfig` own shared C# analysis, and `global.json` pins the SDK.
Game references keep `Private=false`: RimWorld loads DLLs from `Assemblies/`.
Debug/release builds overwrite the same mod assembly path, so the path cannot prove
build mode. Runtime package maintenance belongs to
[Dependencies](../mod/Dependencies/README.md), game-free linked sources to
[C# tests](test-csharp.md), and IPC fixtures to [IPC benchmarks](../bench/ipc/README.md).

Daemon lint policy belongs to `slopd/Cargo.toml` and `slopd/clippy.toml`; CI
checks belong to `.github/workflows/`. Local release recipes belong to
`just/release.just`; `tools/release/latest.py` owns release-mode builds, archive staging,
and rolling remote tag/publication ordering. Release inputs must come from a clean
checkout; mod packaging selects tracked runtime assets and an explicit DLL allowlist
to exclude game assemblies and local files. Installer behavior belongs to
`slopd/src/bin/slopworld/mod_install.rs` and its tests. Profile/window lifetime is
separate; see [profiles](ops-profile.md) and [daemon replacement](daemon-redeploy.md).
