# Building and running

Use the Makefile; `make` prints its target list. On macOS, install GNU Make with
`brew install make` and use it as `gmake`. `RIMWORLD` defaults to
`~/GOG Games/RimWorld/game` and must point to a real install because the mod uses its assemblies.

For a GOG copy, `make gogdl-install` downloads the native Linux build into
`GOGDL_PATH` (default `~/GOG Games`), and `make gogdl-update` updates the existing
`RIMWORLD` directory. Both include owned DLCs. Set `GOGDL_AUTH` if the gogdl token
file is elsewhere; the default is Heroic's `heroic/gog_store/auth.json`. Run
`make gogdl-login` to open the GOG login page, paste its authorization code, and save
the token before downloading.

`BUILD` is `debug` (default) or `release`; `make BUILD=release install` installs
the release build. Build mode is selected with `BUILD`, rather than target aliases. Both builds write
`mod/Assemblies/SlopWorld.dll`, and `lint-mod` always rebuilds it in Release.

The first release uses the canonical `v0.0.1` tag, which embeds `0.0.1` in the daemon and mod
binaries. Untagged Git checkouts use the package version plus the current UTC date and short
`HEAD` hash, for example `0.0.1-20260831-3eb9902`. Source trees without Git retain the `0.0.1`
fallback version.
The release workflow publishes only `v`-prefixed SemVer tags and ships the daemon archive. The
mod assembly is ignored build output: source-based installs and Arch packages rebuild it against
the target RimWorld installation before staging it.

| Target | Does |
| --- | --- |
| `all` | Both halves. |
| `daemon` | `cargo build` in `slopd/`, `--release` under `BUILD=release`. |
| `mod` | Direct Mono `csc` into `mod/Assemblies/SlopWorld.dll`. |
| `test` | `cargo test`, the game-free C# tests in `mod/Tests/`, and the prose-linter tests. |
| `coverage` | Cobertura reports and line/branch summaries for the Rust and game-free C# tests. Requires `cargo-llvm-cov`; restores Coverlet from the repository tool manifest. |
| `format` / `lint` | Both halves; `-daemon` and `-mod` variants exist. |
| `lint-prose` | Find LLM cliches in Markdown and code comments; set `PROSE_LINT_ARGS` to pass paths or CLI options. |
| `install` | `install-daemon` (binary, unit, conditional restart), `install-runner`, `install-mod`, and the bundled UI font. |
| `uninstall` | Undoes the install, including the bundled font. Config and profile are left alone. |
| `gogdl-login` | Opens GOG's login page and saves the gogdl token. |
| `gogdl-install` | Installs the native Linux RimWorld copy from GOG with gogdl. |
| `gogdl-update` | Updates the existing native Linux RimWorld copy with gogdl. |
| `run` | Launches through the runner. `PROFILE` picks the folder. |
| `devloop-sidecar` | Rebuilds/redeploys the sidecar, reinstalls the mod, then runs the game; repeats after the game closes. `SLOPCAR_WORKSPACE` customizes the workspace mount. |
| `mac-setup` | Installs the macOS toolchain and Docker Desktop with Homebrew. |
| `mac-check` | Checks Docker, Mono and the native macOS RimWorld paths. |
| `mac-install` | Builds the sidecar image, runs its doctor, and installs the mod into the configured native macOS app bundle. |
| `mac-run` | Starts the sidecar and launches the configured native macOS game with its isolated profile. |
| `mac-sidecar-stop` / `mac-sidecar-status` / `mac-sidecar-logs` | Manages or inspects the macOS sidecar. |
| `logs` | Tails `Player.log`. |
| `check-reqs` | Reports required host dependencies and detected optional integrations/tools. |
| `harmony` | Fetches the latest official Harmony release into `mod/Assemblies/`. |
| `clean` | Drops build output. |

`make coverage` writes `coverage/rust.cobertura.xml` and
`coverage/csharp.cobertura.xml`. The reports are ignored build output. Use
`coverage-daemon` or `coverage-mod` to measure one half; install the Rust tool with
`cargo install cargo-llvm-cov --locked` when it is not already available. Rust
coverage also needs `llvm-cov` and `llvm-profdata` from the same LLVM release as
the compiler.

`install-mod` uses `slopworld mod install`, the tested host-side Rust command. It stages the six
shipped mod directories beside the destination, replaces only `Mods/SlopWorld`, and refuses a
filesystem root or a destination inside the source tree. Update
`slopd/src/bin/slopworld/mod_install.rs` if the mod gains another top-level directory.

`install-font` puts `assets/fonts/clacon2.ttf` in the current user's
`$XDG_DATA_HOME/fonts` directory (`~/.local/share/fonts` by default) and refreshes fontconfig
when `fc-cache` is available. Override `FONT_DIR` or `FONT_SOURCE` when needed.

`devloop-sidecar` passes `SLOPCAR_CONFIG_DIR` to `slopcar start`, keeping the container's
`endpoint.toml` in the same sidecar config directory that the game launcher reads.

For formatting and auxiliary tools, see [build-tools](build-tools.md). For logs
and runtime checks, see [diagnostics](diagnostics.md).
