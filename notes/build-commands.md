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
the release build. Suffixed targets are aliases. Both builds write
`mod/Assemblies/SlopWorld.dll`, and `lint-mod` always rebuilds it in Release.

| Target | Does |
| --- | --- |
| `all` | Both halves. |
| `daemon` | `cargo build` in `slopd/`, `--release` under `BUILD=release`. |
| `mod` | Direct Mono `csc` into `mod/Assemblies/SlopWorld.dll`. |
| `test` | `cargo test`, the game-free C# tests in `mod/Tests/`, and the prose-linter tests. |
| `coverage` | Cobertura reports and line/branch summaries for the Rust and game-free C# tests. Requires `cargo-llvm-cov`; restores Coverlet from the repository tool manifest. |
| `format` / `lint` | Both halves; `-daemon` and `-mod` variants exist. |
| `lint-prose` | Find LLM cliches in Markdown and code comments; set `PROSE_LINT_ARGS` to pass paths or CLI options. |
| `install` | `install-daemon` (binary, unit, conditional restart), `install-runner`, `install-mod`. |
| `uninstall` | Undoes those three. Config and profile are left alone. |
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

`install-mod` copies loose folders, so a new top-level folder under `mod/` needs
adding to that line.

`devloop-sidecar` passes `SLOPCAR_CONFIG_DIR` to `slopcar start`, keeping the container's
`endpoint.toml` in the same sidecar config directory that the game launcher reads.

For formatting and auxiliary tools, see [build-tools](build-tools.md). For logs
and runtime checks, see [diagnostics](diagnostics.md).
