# Build entry points

Use `just` to list recipes. [Build from source](../docs/src/build.md)
owns toolchains, command usage, tests, coverage, and benchmark workflow.
Common entry points live in `just/popular.just`; imported `.just` files own recipes,
and `just/config.just` owns shared settings and coverage scope. Scripts own multi-step
shell work rather than duplicating it in recipes. Settings accept environment values
or `just NAME=value recipe`; assignments must precede recipe names. Exported settings
carry into recursive calls. `SLOPCAR_PROFILE` stays unexported so native commands do
not select the sidecar profile; sidecar launch recipes set it explicitly.

`packaging/arch/` owns Arch package staging and user setup hooks. The local package
builds a snapshot without private files or build caches; preparation records the
binary version for package metadata, build and check. Cargo fetches dependencies
in preparation and uses frozen builds afterward.
Arch runtime requirements cover the audio library, default shell, core session
infrastructure, Git/worktrees and workspace search. Pager/editor/highlighter tools,
fallback process inspection and desktop/music integrations are optional dependencies.
Pager and highlighter defaults use Auto to select installed tools. The editor still
defaults to micro; optional package metadata does not imply an editor fallback.

`bench/` owns benchmark runners, shared result handling, reporting, and their tests.
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
