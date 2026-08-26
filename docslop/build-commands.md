# Building and running

Use the Makefile; `make` prints its target list. `RIMWORLD` defaults to
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
| `devloop-sidecar` | Rebuilds/redeploys the sidecar, then runs the game; repeats after the game closes. `SLOPCAR_WORKSPACE` customizes the workspace mount. |
| `mac-setup` | Installs the macOS toolchain and Docker Desktop with Homebrew. |
| `mac-check` | Checks Docker, Mono and the native macOS RimWorld paths. |
| `mac-install` | Builds the sidecar image, runs its doctor, and installs the mod into RimWorldMac. |
| `mac-run` | Starts the sidecar and launches native RimWorldMac with its isolated profile. |
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

Agents survive daemon installation restarts: neither tmux nor the game is in the
daemon's cgroup - see [daemon-redeploy](daemon-redeploy.md).

`slopctl logs` shows the last 200 game and daemon lines by default. Select
`game`, `daemon`, or `all`; add `--follow` and pipe the plain output as needed:

```sh
slopctl logs --follow | grep -iE 'error|exception' | head -50
slopctl logs game --lines 500
```

The game source defaults to the conventional Unity `Player.log` path and can
be overridden with `SLOPWORLD_GAME_LOG`. The daemon source reads the user
journal unit `slopd.service`, overridden with `SLOPWORLD_DAEMON_UNIT`. `--json`
emits newline-delimited objects for scripts.

## Formatting

For C#, run `dotnet format` in folder mode with whitespace-only changes;
`.editorconfig` preserves single-line statements. It needs SDK reference
assemblies, while the mod compiler uses direct Mono `csc`: `format-mod` stops without
an SDK, and `lint-mod` still runs compiler warnings. Override `CSC` or `CSC_API` when
the compiler or Mono reference assemblies live elsewhere.

## Diagnostics

```sh
curl -s localhost:7717/api/sessions | python3 -m json.tool
curl -s -X POST localhost:7717/api/sessions/NAME/start
tmux -L slopworld list-sessions
journalctl --user -u slopd -f
slopctl logs --follow
```

`SLOPD_LOG=slopd=debug`; `SLOPD_CONFIG` points at another config file.

## Tools (none run as part of a build)

- `python3 tools/prose_lint.py` (`make lint-prose`) - reports LLM cliches as
  `path:line:column` diagnostics and exits nonzero on an error. Broader density
  and vocabulary rules are advisory unless `--fail-on-warnings` is passed. It
  scans Markdown and only the comments in source files; pass paths, `-` for stdin,
  `--list-rules`, `--rule ID`, `--exclude GLOB`, or `--format json` to narrow or
  integrate it. Code fences and inline code in Markdown are skipped.
  `--commit-msg FILE` accepts the path passed to a Git `commit-msg` hook (or `-`
  for stdin) and ignores Git template comments and verbose diff content.
- `make scheme-report` - measures the three complete UI schemes, including alpha compositing,
  and checks that Warm stays within 5% of SlopWorld's luminance/contrast hierarchy.
- `tools/shot.sh` - grabs the game window. Needs the `x11` preset.
- `python3 tools/loc.py` - counts code. `--docs` adds the markdown;
  `--comments` prints the C# and Rust comments instead of counting them, markers
  stripped and neighbouring lines joined, and `--min=N` keeps only blocks of N
  lines or more - which is how the paragraphs that have grown into documentation
  are found and moved here.
- `tools/roboface.py` - draws the agent faceplates into `mod/Textures/`.
- `tools/fileicons.py` - bakes the files view's icons into `mod/Textures/`.
- `tools/icons.py` (`make icons`) - bakes the action icons out of a Nerd Font's
  Codicons; wants one installed, unlike the others - see
  [mod-icons](mod-icons.md).
- `tools/emoji.py` - bakes an icon from an emoji glyph. An alpha mask by default,
  for the caller to tint; `--color` keeps the face's own colors, which is what a
  thing standing on the map wants - see [mod-jukebox](mod-jukebox.md).
- `tools/emoji_atlas.py` - bakes the supplementary-plane emoji atlas and its generated
  C# code table for the legacy terminal renderer (`make emoji-atlas`).
- `tools/split_ost.py` - crops the newest Bitwig FLAC export at the fixed OST
  boundaries into 192 kbps OGGs in `.ost-staging/`.
- `tools/install_ost.py` - copies the newest staged dated tracks into
  `mod/Sounds/SlopWorld/OST/` and updates `Defs/Songs.xml`; `Radio.cs` points the daemon at
  that directory.
