# Build boundaries

Use `just` to discover recipes and `just --show RECIPE` to inspect them.
Private browser experiments and research belong in ignored `priv/`; public recipes
and CI must not depend on them. The independent [slopweb](slopweb.md) product builds against pinned RimRef assemblies without
an installed game and packages its browser tooling and authored mod UI assets; the installed launcher prepares game-derived files in the user's private
cache. `package-web` selects Arch, Debian, or Fedora without adding browser builds
to the ordinary native product group. [slopweb](../slopweb/README.md) owns usage.
[Build from source](../docs/src/development/build.md) owns setup and validation;
[releases](../docs/src/development/releases.md) owns publication procedures.

Root format/lint/test/clean cover the browser product alongside native components.
`slopweb-restore` and `deps-update-web` cover every browser helper/runtime/test project;
private probes are excluded. `slopweb-clean` preserves compiler caches and private runtimes.

Component justfiles own tool commands; the root owns aggregates and `just/ci.just`
owns CI groups. Imports share one namespace and repository-root working directory.
Use native dependencies for ordering. Python owns substantive generation, staging,
and reporting; run `tools/` as a package through `uv run --locked python -m`.
Routine checks and builds use native quiet options; keep warnings, failures, and
compact test summaries visible.
C# formatting belongs to CSharpier; analyzer policy belongs to `.editorconfig` and
`Directory.Build.props` and runs during builds, not `mod-lint`. Rust lint selection
belongs to each crate's manifest; `clippy.toml` owns thresholds and test allowances.
`slopcar/` owns Linux containers and their host launcher. Mod builds do not
build Rust, so install/launch recipes must declare their launcher dependency.

Shell completion scripts in `packaging/completions/` are generated from the three
client command trees by `just generate-completions`. `tools/cli_completions.py` owns
shell install paths; source installs and release staging consume those scripts.
CLI integration tests check that packaged scripts match the current trees.
Python tooling tests exercise Zsh tab completion when Zsh is installed; tooling CI
includes it.

Ordinary builds consume committed generated files. Regeneration is explicit and
writes only changed bytes. `generate-all` consumes reviewed locks and stages licenses
last; dependency updates are separate. See [runtime packages](../mod/Dependencies/README.md)
for NuGet locks and shipped DLLs. `tools/version/` owns shared product versions;
Rust metadata watches HEAD and refs, never the index.

Release gates recheck checkout identity and cleanliness after validation. Publication
uses the exact verified package artifact without rebuilding or overwriting releases.
Tags must identify the built commit; detached releases must belong to fetched main.
Preview bundles cannot publish. CI uses committed artwork; local releases regenerate.
Sidecar publication also checks its tag against the workflow commit.

Keep Cargo targets separate per checkout; shared fingerprints can reuse another
checkout's artifacts. Coverage retains instrumented dependencies while cleaning
workspace artifacts. Configure compiler flags before CI cache setup, and let
`cargo-llvm-cov` select matching LLVM tools unless explicitly overridden.
Filtered Rust coverage excludes Git's seccomp setup in `git/exec.rs`: its pre-exec
child counters do not survive exec. Child-process enforcement tests still run, and
the raw report retains the file.
The Rust line-coverage gate keeps a small margin below measured hosted coverage:
asynchronous audio, browse, and capture paths can vary between passing runs. Do not
ratchet the gate to a single run's exact percentage.

The pinned formatter and coverage tool run with .NET 10; CI installs the SDK from
`global.json` without an additional runtime. Coverlet permits major-version roll-forward.

The mod project owns game references and compiler settings; `Directory.Build.props`
selects .NET 10 recommended analyzer rules, `.editorconfig` owns diagnostic
severities, and `global.json` pins the SDK.
The mod and runtime-package staging project target .NET 10 for RimWorld 1.7.
`RIMWORLD` defaults to the Steam installation; `MANAGED` defaults to the same root.
The game supplies `Assembly-CSharp.dll`, `UnityStubs.dll`, and `Waylith.dll`.
Game-free CI and hosted packaging compile against the pinned RimRef 1.7 package.
Local builds default to installed assemblies; `mod-build rimref` selects reference
assemblies. Both paths share one NuGet lock graph. Native binding and IL checks
require installed game assemblies and remain part of local release validation.
Never ship game DLLs: keep `Private=false`. Debug and Release overwrite the same
assembly path. See
[C# tests](test-csharp.md) for game-free coverage and [IPC benchmarks](../bench/ipc/README.md)
for benchmark scope. Saved reports live under `bench/results/`.

`tools/release/staging.py` owns product payloads and runtime DLL allowlists.
Native distribution builds compile once and emit three packages: `slopworld`
owns the mod/assets, launcher and desktop integration; `slopd` owns the daemon
and service and depends on `slopctl`; `slopctl` owns the independent CLI and its
completions. `slopcar` and `slopweb` are independent sidecar and browser packages.
Packages have disjoint file ownership and retain coordinated versions. Each
native product carries canonical notices under its own documentation/license
path; notice inventories remain conservative while the Rust crate is shared.
Arch runtime metadata and source payload ownership live in `packaging/arch/packages.sh`.
Source installs mirror these owners: `daemon-install` includes `cli-install`,
`install-mod` installs the launcher, and `daemon-uninstall` retains the CLI.
The root `install`/`uninstall` recipes combine native components. Package
only tracked runtime assets and [notices](core-attribution.md). Build Debian packages
on the target distribution; RPM discovery must exclude private mod assemblies.
Container output publishes only after checks pass. Its temporary mount needs 0755
for Arch's mapped build UID. Disable SELinux relabeling of repositories/worktree metadata.
Sidecar releases build distro packages on native amd64/arm64 runners in
matching-architecture containers. `release-sidecar` builds one architecture;
`package-sidecar` builds a standalone launcher package on the current distribution.
The final release bundle includes their checksums and attestations alongside the
native game packages. Local all-architecture releases require configured container
emulation when the requested architecture differs from the host.
Packages leave game attachment and user-service lifecycle to the user. Source installs
stage a temporary mod for the launcher's atomic installer in `mod_install.rs`.

`bench/runner.py` owns build/run subprocesses. Mod runs use the configured test
project and build configuration through `dotnet run --no-build`, so framework
changes cannot select stale binaries.

`mod-clean` removes the mod DLL and all mod, runtime-package, and test project bin/obj directories.

`tools/host/release_installer.py` is a standalone standard-library-only Python entry
point shipped as `install.py` in native release bundles, covered by checksums and
attestations. Keep it independent of checkout imports. It is also copied to the
website root by `docs-build`. Argument-free piped execution
reconnects stdin to the controlling terminal for the menu and package-manager prompts.
It owns component selection
and downloads; native package managers own installation and dependency resolution.
Host detection prefers OS identity and ancestry, then an unambiguous installed
package manager. Checkbox selection describes desired installation state; cancellation
is distinct from an empty selection. Explicit CLI actions leave unmentioned products
unchanged. Any CLI arguments select noninteractive mode with non-prompting sudo;
only an argument-free invocation opens the menu. Downloads and installation finish before removals; the transactions are
not jointly atomic. It does not attach the mod or change service lifecycle.
