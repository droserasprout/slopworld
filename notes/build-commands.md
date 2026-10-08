# Build entry points

Use `just` to list recipes. [Build from source](../docs/src/development/build.md)
owns toolchains, command usage, tests, coverage, and benchmark workflow.
Common entry points live in `just/popular.just`; imported `.just` files own recipes,
and `just/config.just` owns shared settings and coverage scope.
Recipe names put the action first, followed by the component (`daemon`, `mod`, or
`tools`) and any qualifier: `format-mod`, `check-format-daemon`,
`refresh-daemon-licenses`. Bare `daemon` and `mod` are the build entry points;
`refresh` and `refresh-*` own explicit regeneration. Use one canonical name per recipe.
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
own their launcher build dependency. Regeneration is an explicit, isolated step
after the main source changes: run `just refresh` for all outputs, or the relevant
`refresh-protocol`, `refresh-api-docs`, `refresh-daemon-licenses`, `refresh-reference`,
or asset recipe,
then review its diff and validate. The aggregate refresh stages licenses last;
dependency lock updates remain separate. Builds, tests, lint, benchmarks, and docs builds
consume existing generated files. `check-generated` compares temporary protocol
outputs without modifying the checkout. Explicit generators publish only changed
bytes to preserve compiler input timestamps.
`devloop` runs `just install run` together, sharing build dependencies once per
iteration and retaining each worktree's Rust target and C# obj directories.
Only an explicit `just clean` removes build output.
Rust version metadata watches Git HEAD and tag/ref storage, not the index:
staging and index refreshes must not invalidate daemon builds.

`just ci` checks generated files, formatting and tool/pager behavior before lint
and coverage. The test workflow exposes those stages separately for timings.
CI installs pinned tools, including uv, through
`.github/actions/setup-build-tools/`. It owns archive caching, tool versions,
and the protoc checksum for generated bindings.
Push triggers skip the test workflow for changes confined to Markdown, `docs/`,
and `notes/`. Documentation publication filters its book, repository README/note,
and tooling inputs; sidecar publication filters Docker build inputs, canonical
licenses, and its workflow. Manual runs and reusable test calls bypass push path
filters. Keep the image path list aligned with Dockerfile inputs and `.dockerignore`.
The C# formatter opens only its source directories: folder discovery scans for
editor configs before applying file exclusions, so opening the checkout root can
walk unrelated container storage under `dist/`.
`.github/workflows/image.yml` owns sidecar publication to
`ghcr.io/<repository-owner>/slopcar` for amd64 and arm64. Main pushes and manual
runs publish full commit SHA tags; only runs on `main` update `latest`.
Only superseded branch-push test runs are cancelled; called tests and
manual runs have isolated concurrency groups. NuGet caching includes the locked
runtime/test dependency graphs and the coverage tool manifest.

`packaging/arch/` owns Arch package staging and user setup hooks. The local package
builds a snapshot without private files or build caches; preparation records the
binary version for package metadata, build and check. Both source PKGBUILDs build
the daemon and mod before optional checks. `tools/release/source_mod.py` stages
their mods and local source installs using the release asset-directory and runtime
DLL allowlists, including themes, and copies notices from canonical sources.
Cargo and uv fetch locked dependencies
in preparation; later builds use frozen Cargo dependencies and offline uv execution.
Arch runtime requirements cover the audio library, default shell, core session
infrastructure, Git/worktrees and workspace search. Pager/editor/highlighter tools,
fallback process inspection and desktop/music integrations are optional dependencies.
Pager and highlighter defaults use Auto to select installed tools. The editor still
defaults to micro; optional package metadata does not imply an editor fallback.

`packaging/debian/` owns Debian binary metadata and installed user setup instructions.
`tools/release/debian.py` builds native Debian/Ubuntu packages through `pkg-debian`;
`tools/release/latest.py` shares the mod asset/runtime DLL allowlist with it.
Rolling releases include both native package formats. `tools/release/arch.py`
uses the existing Arch PKGBUILD metadata with a staged release payload and lets
makepkg own package metadata/mtree generation.
Arch archive validation uses an empty staging pacman database, so file queries do
not depend on the host's installed package database.
`tools/release/container_debian.py` rebuilds Rust in the pinned Debian toolchain
from `packaging/debian/Dockerfile`;
Debian packaging and its real archive tests run there before publication.
The container trusts only the mounted checkout via Git's process environment;
this allows Git reads when Docker preserves a different host owner.
Native rolling asset names stay stable, while package metadata carries full versions.
The aggregate release computes checksums only after all four artifacts succeed.
Debian shared-library dependencies come from the build host's `dpkg-shlibdeps`,
so packages must be built on the target distribution rather than from Arch binaries.
System packages stage the mod under `/usr/share/slopworld` and leave game attachment
and user-service lifecycle to the user. Package builds publish only after all staging,
dependency discovery, and archive construction succeed.

`tools/` is a repository Python package, with focused subpackages and tests beside
their owners. `tools.ROOT` owns checkout paths; Python recipes use `uv run --locked python -m`
and package imports rather than adding script directories to `sys.path`.
`pyproject.toml` declares Python dependencies and `uv.lock` pins them. uv maintains
the ignored `.venv/` without installing the repository package. Asset generators
opt into the `assets` extra. The uv `dev` group owns Ruff, strict mypy, pytest,
pytest-cov, and the dependencies needed to type-check asset generators.
`just lint-tools` formats sources and sorts imports before checking core correctness
rules and running strict mypy across `tools/`, including its tests. Optional rendering
backends without stubs have scoped missing-import overrides; package code remains strict.
`just test-tools` validates shared inputs and runs package tests with branch coverage, excluding test
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
owns SlopWorld terms. `just refresh-licenses` stages ignored copies into
`mod/About/ThirdPartyNotices/` and `mod/About/LICENSE`; `just check-licenses`
verifies their contents and rejects stale extra files. Both are manual maintainer
commands; builds, installation, and packaging do not invoke them.
Source installation stages a temporary mod through `tools/host/install_mod.py`
before invoking the launcher's atomic installer. Source installation and release
packaging copy canonical notices directly into their staging directories, ignoring
local refreshed copies. License tests use temporary directories. The launcher
copies its supplied mod tree's `About/` recursively.
Release archives, Arch packages, and sidecar images also include readable copies
from the canonical sources. Update only the canonical files.
`just refresh-daemon-licenses` refreshes the Rust inventory from the locked Cargo graph,
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
