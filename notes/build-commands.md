# Build boundaries

Use `just` to discover recipes and `just --show RECIPE` to inspect them.
[Build from source](../docs/src/development/build.md) owns setup and validation;
[releases](../docs/src/development/releases.md) owns publication procedures.

Component justfiles own tool commands; the root owns aggregates and `just/ci.just`
owns CI groups. Imports share one namespace and repository-root working directory.
Use native dependencies for ordering. Python owns substantive generation, staging,
and reporting; run `tools/` as a package through `uv run --locked python -m`.
Routine checks and builds use native quiet options; keep warnings, failures, and
compact test summaries visible.
`mac/` owns native macOS workflows; `slopcar/` owns containers. Mod builds do not
build Rust, so install/launch recipes must declare their launcher dependency.

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

The mod project owns game references. Never ship game DLLs: keep `Private=false`.
Pinned RimRef selects explicit references with `UseRimRef=true` and shares the game
build's lock. Debug and Release overwrite the same assembly path. See
[C# tests](test-csharp.md) for game-free coverage and [IPC benchmarks](../bench/ipc/README.md)
for benchmark scope. Saved reports live under `bench/results/`.

`tools/release/staging.py` owns shared payloads and runtime DLL allowlists. Package
only tracked runtime assets and [notices](core-attribution.md). Build Debian packages
on the target distribution; RPM discovery must exclude private Unity assemblies.
Container output publishes only after checks pass. Its temporary mount needs 0755
for Arch's mapped build UID. Disable SELinux relabeling of repositories/worktree metadata.
Packages leave game attachment and user-service lifecycle to the user. Source installs
stage a temporary mod for the launcher's atomic installer in `mod_install.rs`.
