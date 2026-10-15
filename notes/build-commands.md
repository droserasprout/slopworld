# Build entry points

Use `just` to list recipes. [Build from source](../docs/src/development/build.md)
owns toolchains, command usage, tests, coverage, and benchmark workflow.
Imported recipe files share one flat namespace. Commands use component prefixes:
`just daemon-build`, `just mod-test`, and `just generate-protocol`.
Use `just --show RECIPE` to inspect a command. Root `build`, `install`, `format`, `lint`, `test`, `clean`, and `uninstall`
recipes combine components through dependencies. `generate-all` refreshes all generated outputs.
Keep one canonical public command and migrate callers when it changes.

`rust-toolchain.toml` owns the Rust version and required components for checkout builds.
The test workflow reads that file when installing Rust. Agent sandboxes with
`rust-cache` share installed host toolchains and writable rustup download/staging
caches. The base `rust` preset alone only exposes installed toolchains read-only.

Just owns command discovery, argument validation and prerequisite ordering.
Component recipe files own tool commands; the root justfile owns aggregate prerequisites,
and `just/ci.just` owns CI validation stages. Sidecar operations live under the `sidecar-` prefix.
`just/package.just` owns native packages, archive tests and source-mod staging;
`just/image.just` owns distribution release compiler images; `sidecar-image` owns
the sidecar runtime image.
`just/release.just` owns version bumps, tagged release archives and publication. Specialist recipe files
stay focused on their workflow.

`just/config.just` owns shared build/tool settings; focused
`just/settings-*.just` files and `mac/config.just` own domain settings.
Repository project paths and tool names are fixed at their call sites; use `PATH`
to select installed tools. Keep exported overrides for user paths and workflow choices.
All files are imported by the root justfile and share its settings, variables, and
repository-root working directory. Use native dependencies for ordering. Variadic
recipes must be last in an invocation. Test filters and benchmark options pass
directly to their tools; coverage exclusions live beside the Rust collector. Keep command routing and short orchestration in Just; Python
owns substantive generation, staging, reporting and publication logic.

`mac/` owns native macOS checks, workflow code and settings; `just/mac.just` exposes
its commands as `just mac-*`. `mac/setup.sh` bootstraps uv before Python recipes run.
Settings accept environment values or `just NAME=value recipe`; assignments precede
recipe names. Sidecar launch commands pass `--sidecar` explicitly.
Runtime path defaults belong to the shared Rust resolver; recipes query the
launcher or CLI rather than reproducing those defaults.

Mod compilation does not build Rust binaries. `tools/version/mod_version.py` reads the
Cargo package fallback and uses `tools/version/resolve.py` for the same tag/date/commit
rules as Rust; `VERSION` overrides either build. `tools/version/metadata.py` owns
the shared Python release-tag syntax and Cargo version reader; release tags always
use `vMAJOR.MINOR.PATCH`, while artifact versions omit `v`. CI validates tags with
that module before container setup. `tools/version/product.py` owns the shared
editable product-version file list;
`just release-bump-version VERSION` validates all replacements and a dated changelog entry
before writing. Both release gates require those product versions to match the tag.
Installer and launch recipes
own their launcher build dependency. Regeneration is an explicit, isolated step
after the main source changes: run `just generate-all` for all outputs, or the relevant
`generate-protocol`, `generate-api-docs`, `generate-daemon-licenses`,
or asset recipe,
then review its diff and validate. The aggregate refresh includes daemon and sidecar
license generation and stages licenses last. `deps-update` explicitly updates all
Rust, Python, and NuGet graphs and regenerates notices.
Rust and Python updates stay within manifest constraints; NuGet re-resolves the
declared package versions without compiling or copying runtime DLLs. Review lock
changes and synchronize runtime assemblies before validation. Release-time `generate-all`
consumes reviewed dependency locks rather than querying newer versions.
Builds, tests, lint, benchmarks, and docs builds
consume existing generated files. Release targets explicitly run the full refresh
aggregate before lint and tests, rejecting any resulting tracked changes. `generate-check-protocol` compares temporary protocol
outputs without modifying the checkout. Explicit generators publish only changed
bytes to preserve compiler input timestamps.
`devloop` runs `just install run` together, retaining each worktree's Rust target and C# obj directories.
Only an explicit `just clean` removes ordinary build output. `daemon-coverage`
cleans workspace artifacts and profile data with `cargo llvm-cov clean --workspace`,
retaining instrumented dependencies under `slopd/target/llvm-cov-target` locally and in CI.
The Rust cache includes that nested target directory. Its restore prefix isolates
the compiler and Rust/Cargo environment, plus the pinned build-tool setup, toolchain
file and coverage collector. Keep compilation flags visible at job scope before
cache setup.
`cargo-llvm-cov` owns coverage-tool selection from the active Rust toolchain;
the collector preserves explicit `LLVM_COV`/`LLVM_PROFDATA` overrides without
selecting potentially incompatible system tools from `PATH`.
Host recipes use sccache when installed, respecting an explicit `RUSTC_WRAPPER`
(including empty to disable it). `SCCACHE_DIR` defaults to `~/.cache/sccache`, also
shared by the opt-in `rust-sccache` sandbox preset. `rust-cache` grants writable access
to Cargo caches, installed Rust toolchains, update hashes, and rustup download/staging
caches; `slopworld-debug` selects `rust-sccache`. The daemon creates preset cache
directories at launch; host sccache creates its cache itself.
Keep Cargo target directories separate per checkout; sharing
fingerprints and build-script outputs can reuse another checkout's artifacts.
Incremental compilation remains enabled; sccache reuses eligible dependencies.
Rust version metadata watches Git HEAD and tag/ref storage, not the index:
staging and index refreshes must not invalidate daemon builds.

`just/ci.just` is imported at root scope so CI groups use native component dependencies.
`just ci` runs all three groups locally. The test workflow resolves the requested
ref once, then runs `ci-rust`, `ci-tools`, and `ci-mod` in parallel jobs against that commit. Rust and pager checks use Debian forky with the pinned tmux;
only the Rust job needs a privileged container for sandbox tests. Rust and C# jobs
publish separate `coverage-rust` and `coverage-mod` artifacts and summaries.
The tooling job installs the pinned Rust toolchain for protocol-generator rustfmt
calls, without a Cargo build cache.
`coverage-summary-rust` and `coverage-summary-mod` summarize their respective reports;
`coverage-summary` combines saved reports locally.
Repository-owned jobs pin Ubuntu 24.04 so runner OS migrations are explicit.
GitHub checkout, cache, .NET setup, and artifact actions use their Node.js 24 majors.
CI installs pinned tools through `.github/actions/setup-build-tools/`.
It delegates just and cargo-llvm-cov installation to `taiki-e/install-action`,
and uv installation and dependency caching to `astral-sh/setup-uv`.
The local action owns tool versions and the checksum-verified protoc installer/cache;
Rust builds and generated-binding checks request protoc. Documentation and C# setup skip it.
Push triggers skip the test workflow for changes confined to Markdown, `docs/`,
and `notes/`. Documentation publication filters its book, repository README/note,
and tooling inputs on `main`, `human-docs`, and `road-to-release`, publishing each
branch to Cloudflare Pages; sidecar publication runs only on `vMAJOR.MINOR.PATCH` tag pushes. Manual test runs and reusable test calls bypass push path
filters.
`mod-format` and `mod-lint` use the CSharpier version pinned in
`.config/dotnet-tools.json`, restored by a private recipe prerequisite. They open only
the C# source directories; `.csharpierignore` excludes generated bindings,
build output, and project files. CSharpier owns layout; `.editorconfig` supplies
indentation, line endings, and the shared .NET analyzer policy. Generated emoji
metadata in `UI/Text/TextSpriteData.cs` also keeps its generator-owned layout.
`.github/workflows/image.yml` owns sidecar publication to
`ghcr.io/<repository-owner>/slopcar` for amd64 and arm64. Only `vMAJOR.MINOR.PATCH` tag
pushes build and publish images, using full commit SHA and release-version tags.
There are no branch or manual triggers. Tag validation runs before container setup.
CI passes the same version into the Docker build and OCI label.
Docker's `github-builder` reusable workflow owns native platform runners, separate
platform caches, and manifest assembly. It publishes the release-version and
full-commit tags after both builds succeed; the calling workflow owns tag validation
and the version build argument and labels.
Only superseded branch-push test runs are cancelled; called tests and
manual runs have isolated concurrency groups. NuGet caching includes the locked
runtime/test dependency graphs and the coverage tool manifest.

`packaging/arch/` owns Arch package staging and user setup hooks. The local package
builds a snapshot without private files, daemon/sidecar and benchmark targets,
C# build output, or coverage output; preparation records the
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
`tools/release/native.py` owns the Arch/Debian/Fedora packaging CLI and local build workflow;
`tools/release/arch.py`, `tools/release/debian.py`, and `tools/release/fedora.py`
own native archive formats. `package-arch`, `package-debian`, and `package-fedora`
build native packages; `package-test DISTRIBUTION` tests archives. The corresponding
`image-arch`, `image-debian`, and `image-fedora` recipes build release compiler images. Package builds do not install their output.
`tools/release/staging.py` owns common system payloads, permissions, notices,
mod asset/runtime DLL allowlists, and tracked-release staging used by package builders. `tools/release/source_mod.py`
selects whole asset directories for source snapshots without Git metadata.
Versioned releases include Arch, Debian, and Fedora native package formats. `tools/release/arch.py`
uses the existing Arch PKGBUILD metadata with a staged release payload and lets
makepkg own package metadata/mtree generation.
Arch archive validation uses an empty staging pacman database, so file queries do
not depend on the host's installed package database.
`tools/release/container.py` owns x86_64 container builds, cache mounts, checkout
access, and atomic artifact handoff. The native format modules own version
conversion, and the container runner selects release filenames. The `packaging/`
Dockerfiles select distribution and toolchain tags without image digest pins.
Arch containers require privileged mode for makepkg's nested Bubblewrap namespace.
Containers test real archives before packaging and write to fresh temporary output,
so stale or empty packages cannot replace an existing artifact. Git trusts only
the mounted checkout; SELinux labeling is disabled to avoid relabeling repositories
and linked-worktree metadata.

`packaging/fedora/` owns RPM metadata and installed setup instructions;
`tools/release/fedora.py` owns payload staging and publication. RPM discovers ELF
dependencies but excludes private mod assemblies from requires/provides.
Snapshot hyphens become dots in RPM metadata; mod metadata keeps the build version.
Release archives normalize directories and executables to `0755` and data to
`0644`, independent of source permissions and the build umask.
The aggregate release computes checksums only after all five artifacts succeed.
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
`just tools-format` applies formatting and import sorting; `tools-fix` applies lint
fixes. `tools-lint` checks formatting and Ruff rules without rewriting sources and runs
strict mypy across `tools/`, including tests. `mod-lint` checks C# formatting;
`mod-check-build` validates Release compilation and native text/swapchain bindings against
game assemblies without starting the game. Optional rendering
backends without stubs have scoped missing-import overrides; package code remains strict.
`just tools-test` runs pytest with discovery rooted at tools, mac, slopcar, and bench
in `pyproject.toml`; explicit paths and options select a subset. This includes the
benchmark unittest suites; pytest adds their standalone script directory to its
import path for sibling imports. `tools-test-pager` owns tmux/less integration checks;
`sidecar-test` owns launcher Rust tests. Root `test` includes daemon, sidecar, C# and `test-tools` suites.
`test-tools` combines Python, pager and benchmark-helper checks and is shared by
local tests and tooling CI. Release validation uses the same root test suite.
These checks do not run performance benchmarks.
`just tools-coverage` explicitly collects Python branch coverage; normal tests and CI
do not collect it. `just deps-update-tools` updates dependency resolution.

`tools/utils.py` owns shared command parsing, subprocess execution, logging, and CLI
failure handling. Command settings use shell-style argument quoting without executing
a shell. `tools/host/` owns daemon installation, GOG setup, and worktree development loops.
Component Just recipes own coverage collection; `tools/coverage/coverage_summary.py`
owns report parsing. Collection writes reports; `coverage-summary` prints saved reports.

The daemon installer compares running executable contents and the managed unit
fragment; systemd drop-ins are outside that comparison. Inactive units activate once
through restart. GOG login captures the CLI token response and verifies success before
accepting saved credentials.

`bench/runner.py` owns benchmark build/run subprocesses; `bench/report.py` owns
collection and reporting. It calls the runner directly, building only the selected
suite. Mod runs use the same configured test project and build configuration through
`dotnet run --no-build`, so target-framework changes cannot select stale binaries.
Benchmark recipes forward CLI options instead of exporting each option as a
shared setting. `bench/` also owns shared result handling and its tests.
`slopcar/Cargo.toml` owns the independent native sidecar launcher, built by
`just sidecar-build` and installed by `sidecar-install`. `sidecar-test` and
`sidecar-lint` validate it without Docker or the game. Recipes export `SLOPCAR`
to both Python callers; runtime commands use the embedded seccomp policy.
`generate-sidecar-licenses` owns the launcher's full Linux/macOS dependency notices
under `licenses/slopcar/`, also embedded in the executable.
`slopcar/` owns the shared container devloop; platform workflows call it through `just`.
A failed image build or mod installation ends the iteration before container replacement;
a startup failure prevents game launch. The outer loop retries after failed iterations.
An absent container during removal is tolerated.
`tools/assets/` owns asset generators and text-sprite checks; `assets/` owns bundled
source data and icon manifests. Generated runtime assets stay in `mod/`.
OST production scripts and staged audio belong to the private repository under
`priv/ost/`; shipped soundtrack files and song definitions belong to `mod/`.

The [attribution policy](core-attribution.md) applies to used libraries and assets.
`licenses/` owns canonical third-party texts and attribution; the root `LICENSE`
owns SlopWorld terms. `just generate-licenses` stages ignored copies into
`mod/About/ThirdPartyNotices/` and `mod/About/LICENSE`; `just generate-check-licenses`
verifies their contents and rejects stale extra files. Both are maintainer commands; release targets run them through their validation
stages. Ordinary builds and installation do not invoke them.
Source installation stages a temporary mod through `tools/host/install_mod.py`
before invoking the launcher's atomic installer. Source installation and release
packaging copy canonical notices directly into their staging directories, ignoring
local refreshed copies. License tests use temporary directories. The launcher
copies its supplied mod tree's `About/` recursively.
Release archives, native packages, and sidecar images also include readable copies
from the canonical sources. Update only the canonical files.
`just generate-daemon-licenses` refreshes the Rust inventory from the locked Cargo graph,
including build/dev dependencies and all target platforms. Cargo manifests own
its license metadata; the generated table does not replace distribution notices.

The mod project owns game references/compiler settings; `Directory.Build.props`
and `.editorconfig` own shared C# analysis, and `global.json` pins the SDK.
The mod and its runtime-package staging project target .NET 10 for RimWorld 1.7.
`RIMWORLD` defaults to the Steam installation; `MANAGED` defaults to that same root.
The project references `Assembly-CSharp.dll`, `UnityStubs.dll`, and `Waylith.dll`
with `Private=false`: RimWorld loads DLLs from `Assemblies/`.
The game supplies framework assemblies; the release allowlist and runtime staging
must include only SlopWorld, Harmony, and package dependencies.
Debug/release builds overwrite the same mod assembly path, so the path cannot prove
build mode. Runtime package maintenance belongs to
[Dependencies](../mod/Dependencies/README.md), game-free linked sources to
[C# tests](test-csharp.md), and IPC fixtures to [IPC benchmarks](../bench/ipc/README.md).

Daemon lint policy belongs to `slopd/Cargo.toml` and `slopd/clippy.toml`; CI
checks belong to `.github/workflows/`. Local release recipes belong to
`just/release.just`; `tools/release/publish.py` owns release-mode builds, archive staging,
and versioned remote tag/publication ordering. Existing release tags must match the
built commit, and existing releases are never overwritten. `tools/release/gates.py`
requires a clean `main` checkout with one `vMAJOR.MINOR.PATCH` tag at HEAD for both release
targets. It runs all refreshes, lints, and tests, generated/license/text-sprite checks,
and docs before building. Every stage rechecks checkout identity and cleanliness;
`VERSION` cannot override the tag. `tools/release/changelog.py` reads the matching
dated human-maintained changelog entry for the release description and rejects
missing or ambiguous entries. Release inputs must come from a clean checkout; mod packaging selects tracked runtime assets and an explicit DLL allowlist
to exclude game assemblies and local files. Installer behavior belongs to
`slopd/src/bin/slopworld/mod_install.rs` and its tests. Profile/window lifetime is
separate; see [profiles](ops-profile.md) and [daemon replacement](daemon-redeploy.md).
