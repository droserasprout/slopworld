# Build entry points

Use `make` (`gmake` on macOS) to list targets. [Build from source](../docs/src/build.md)
owns toolchains, command usage, tests, coverage, and benchmark workflow.
Common entry points live in `make/popular.mk`; target-specific fragments own recipes,
and `make/config.mk` owns shared settings and coverage scope. Scripts own multi-step
shell work rather than duplicating it in recipes.

The mod project owns game references/compiler settings; `Directory.Build.props`
and `.editorconfig` own shared C# analysis, and `global.json` pins the SDK.
Game references keep `Private=false`: RimWorld loads DLLs from `Assemblies/`.
Debug/release builds overwrite the same mod assembly path, so the path cannot prove
build mode. Runtime package maintenance belongs to
[Dependencies](../mod/Dependencies/README.md), game-free linked sources to
[C# tests](test-csharp.md), and IPC fixtures to [IPC benchmarks](../bench/ipc/README.md).

Daemon lint policy belongs to `slopd/Cargo.toml` and `slopd/clippy.toml`; CI/release
behavior belongs to `.github/workflows/`. Installer behavior belongs to
`slopd/src/bin/slopworld/mod_install.rs` and its tests. Profile/window lifetime is
separate; see [profiles](ops-profile.md) and [daemon replacement](daemon-redeploy.md).
