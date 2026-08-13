# Building and running

Everything goes through the Makefile; `make` alone prints the target list (each
target carries a `##` line). `RIMWORLD` defaults to `~/RimWorld/game` and must
point at a real install - the mod builds against the game's own assemblies.

`BUILD` is `debug` (the default) or `release`, and every target follows it -
including `install`, so a packaged-quality install is `make BUILD=release
install`. The `-debug` and `-release` suffixed targets are aliases that set it.
Both configurations write the mod to the same `mod/Assemblies/SlopWorld.dll`, so
nothing on disk says which one is there; `lint-mod` rebuilds that file in
Release whatever `BUILD` says.

| Target | Does |
| --- | --- |
| `all` | Both halves. |
| `daemon` | `cargo build` in `slopd/`, `--release` under `BUILD=release`. |
| `mod` | msbuild into `mod/Assemblies/SlopWorld.dll`. |
| `test` | `cargo test`. The mod has no harness; it needs the game. |
| `format` / `lint` | Both halves; `-daemon` and `-mod` variants exist. |
| `install` | `install-daemon` (binary, unit, restart), `install-runner`, `install-mod`. |
| `uninstall` | Undoes those three. Config and profile are left alone. |
| `redeploy` | `install`, then `POST /api/game/restart`. |
| `run` | Launches through the runner. `PROFILE` picks the folder. |
| `logs` | Tails `Player.log`. |
| `check-reqs` | Reports required host dependencies and detected optional integrations/tools. |
| `clean` | Drops build output. |

`install-mod` copies loose folders, so a new top-level folder under `mod/` needs
adding to that line.

`make redeploy` needs `daemon.game_cmd` (default `~/.local/bin/slopworld`).
Agents survive it: neither tmux nor the game is in the daemon's cgroup - see
[daemon-redeploy](daemon-redeploy.md).

`slopctl logs` is the host-side, pipeline-friendly diagnostic command. It shows
the last 200 lines from both sources by default; choose `game`, `daemon` or
`all`, add `--follow`, and pipe the plain output through tools such as `grep`
and `head`:

```sh
slopctl logs --follow | grep -iE 'error|exception' | head -50
slopctl logs game --lines 500
```

The game source defaults to the conventional Unity `Player.log` path and can
be overridden with `SLOPWORLD_GAME_LOG`. The daemon source reads the user
journal unit `slopd.service`, overridden with `SLOPWORLD_DAEMON_UNIT`. `--json`
emits newline-delimited objects for scripts.

## Formatting

`.editorconfig` exists for the C# half: `dotnet format` takes its whole layout
from there and on defaults would rewrite the codebase rather than tidy it -
`csharp_preserve_single_line_statements` is what keeps a guard clause a guard
clause. Run in *folder* mode, whitespace only, because loading a net472 project
wants reference assemblies. It is an SDK command while the mod builds under
mono's msbuild, so a machine that can build may have no SDK: `format-mod` says so
and stops, `lint-mod` carries on with the compiler's own warnings.

## Poking at it

```sh
curl -s localhost:7717/api/sessions | python3 -m json.tool
curl -s -X POST localhost:7717/api/sessions/NAME/start
tmux -L slopworld list-sessions
journalctl --user -u slopd -f
slopctl logs --follow
```

`SLOPD_LOG=slopd=debug`; `SLOPD_CONFIG` points at another config file.

## Tools (none run as part of a build)

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
