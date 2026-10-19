# Build entry points

Use `just` to discover commands and `just --show RECIPE` to inspect them.
[Build from source](../docs/src/development/build.md) owns setup and validation
procedures; [Packaging and releases](../docs/src/development/releases.md) owns
release procedures. This note owns dependency maintenance and benchmark workflows.

Just owns command discovery, argument validation, and prerequisite ordering.
Component recipe files own tool commands; the root justfile owns aggregates and
`just/ci.just` owns CI groups. Imports share one namespace and repository-root
working directory. Keep one public command per operation and use native dependencies
for ordering. Python owns substantive generation, staging, reporting, and publication.
`tools/` is a package with tests beside their owners; invoke its modules through
`uv run --locked python -m` rather than modifying `sys.path`.

`just/config.just` and focused settings files own build overrides. Runtime path
defaults belong to the shared Rust resolver; recipes query the launcher or CLI.
`mac/` owns native macOS workflows; `slopcar/` owns the shared container workflow.
Mod compilation does not build Rust binaries, so installer and launch recipes
must declare their launcher dependency.

## Generated inputs and versions

Ordinary builds and checks consume existing generated files. Regeneration is an
explicit step whose diff must be reviewed. Generators publish only changed bytes
so they do not invalidate compiler inputs unnecessarily. `generate-all` stages
licenses last and consumes reviewed dependency locks. See
[Dependency maintenance](#dependency-maintenance) for updates and runtime DLL synchronization.

`tools/version/` owns shared version resolution, release-tag syntax, and the
product-version file list. Rust and mod builds must resolve the same version.
Rust version metadata watches HEAD and tag/ref storage, never the index: staging
files must not invalidate builds. `tools/release/gates.py` rechecks checkout
identity and cleanliness after every validation stage. Publication belongs to
`tools/release/publish.py`; existing tags must match the built commit and existing
releases must not be overwritten.
Manual sidecar image publication requires the selected release tag to resolve to
the workflow commit: the reusable image builder builds that commit, independently
of the preparation job's checkout.

## Dependency maintenance

To prepare dependencies from the reviewed locks, run `just deps-restore`.
To update every dependency graph and regenerate license notices, run `just deps-update`.
It upgrades Rust and Python dependencies within their manifest constraints and
re-resolves NuGet locks at the versions declared in the projects.
Container images and system packages follow their configured tags and repositories.

For a Python-only update, use `just deps-update-tools` and commit `uv.lock`.
After NuGet changes, run `just mod-restore`, review the locks and shipped runtime
DLLs under `mod/Assemblies`, and run `just mod-test`. Follow
[Runtime package maintenance](../mod/Dependencies/README.md)
when changing those packages.

Review and commit the resulting changes. `just generate-all` regenerates outputs
from reviewed inputs; it does not upgrade dependencies.

Asset generators use uv's optional `assets` dependencies and need their native
graphics libraries. For example, inspect the emoji generator's options with:

```sh
uv run --locked --extra assets python -m tools.assets.emoji --help
```

## Benchmarks

Run all game-free benchmark suites in Release mode:

```sh
just BUILD=release bench-run gamefree --run <name>
```

Reports go to `bench/results/<name>/`. Replace `gamefree` with `daemon`, `mod`,
or `ipc` to select one suite; only its binaries are built.
IPC benchmarks and the terminal-input HTTP regression require Mono.

Use `just bench-report --run <name>` to render saved measurements, or
`just bench-report --run <name> --baseline <older> --mode relative` to compare runs.
Pass `--help` to a benchmark operation for its CLI options. See the
[IPC benchmark suite](../bench/ipc/README.md)
for its scope.

## Build isolation

Keep Cargo target directories separate per checkout; shared fingerprints and
build-script outputs can reuse another checkout's artifacts. Share dependency
caches through the [sandbox presets](../docs/src/sandbox/sandbox-presets.md).
Coverage retains instrumented dependencies while cleaning workspace artifacts.
CI cache keys must include the compiler environment and collector inputs; set
compilation flags before cache setup. Let `cargo-llvm-cov` select matching LLVM
tools while preserving explicit `LLVM_COV` and `LLVM_PROFDATA` overrides.

The mod project owns game references and compiler settings. Game references keep
`Private=false` to avoid shipping game DLLs. The mod project's pinned RimRef package
uses `ExcludeAssets=all` and `PrivateAssets=all`; `UseRimRef=true` selects only the
explicit game references from its generated package path. Both build modes share
one NuGet lock. `just mod-build rimref` selects this mode, and `ci-mod` checks its
Release build. Installed-game builds remain the default. Debug and Release overwrite the same
assembly path, so that path cannot establish build mode. See
[C# tests](test-csharp.md) for game-free source linking.

## Distribution boundary

`tools/release/staging.py` owns shared payloads and runtime DLL allowlists;
`source_mod.py` handles source installs without Git metadata. Release staging
selects tracked runtime assets and excludes game assemblies and local files.
Source installs and packages copy notices from canonical `licenses/` files,
not locally generated copies; see [attribution](core-attribution.md).

Native format modules under `tools/release/` own archive construction and version
conversion; `container.py` owns container builds and artifact handoff. Build Debian
binaries on the target distribution because dependency discovery uses its host
libraries. RPM dependency discovery must exclude private Unity assemblies.
Containers write fresh temporary output and publish only after successful checks,
so stale or empty artifacts cannot replace existing packages. SELinux labeling is
disabled to avoid relabeling repositories and linked-worktree metadata.

Packages leave game attachment and user-service lifecycle to the user. Source
installation stages a temporary mod before the launcher's atomic installer;
`slopd/src/bin/slopworld/mod_install.rs` and its tests own that contract.
Profile and process lifetime belong to [profiles](ops-profile.md) and
[daemon replacement](daemon-redeploy.md).
