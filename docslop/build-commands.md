# Building and running

Use the Makefile; `make` prints its target list. `RIMWORLD` defaults to
`~/RimWorld/game` and must point to a real install because the mod uses its assemblies.

`BUILD` is `debug` (default) or `release`; `make BUILD=release install` installs
the release build. Suffixed targets are aliases. Both builds write
`mod/Assemblies/SlopWorld.dll`, and `lint-mod` always rebuilds it in Release.

| Target | Does |
| --- | --- |
| `all` | Both halves. |
| `daemon` | `cargo build` in `slopd/`, `--release` under `BUILD=release`. |
| `mod` | Direct Mono `csc` into `mod/Assemblies/SlopWorld.dll`. |
| `test` | `cargo test` and the game-free C# tests in `mod/Tests/`. |
| `format` / `lint` | Both halves; `-daemon` and `-mod` variants exist. |
| `install` | `install-daemon` (binary, unit, conditional restart), `install-runner`, `install-mod`. |
| `uninstall` | Undoes those three. Config and profile are left alone. |
| `run` | Launches through the runner. `PROFILE` picks the folder. |
| `logs` | Tails `Player.log`. |
| `check-reqs` | Reports required host dependencies and detected optional integrations/tools. |
| `harmony` | Fetches the latest official Harmony release into `mod/Assemblies/`. |
| `clean` | Drops build output. |

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
- `tools/split_ost.py` - crops the newest Bitwig FLAC export at the fixed OST
  boundaries into 192 kbps OGGs in `.ost-staging/`.
- `tools/install_ost.py` - copies the newest staged dated tracks into
  `mod/Sounds/SlopWorld/OST/` and updates `Defs/Songs.xml`; `Radio.cs` points the daemon at
  that directory.
