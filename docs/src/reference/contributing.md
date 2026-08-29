# Contributing

## Repository layout

```text
slopd/          Rust daemon and launcher
  src/
    bin/        slopworld (launcher), slopctl (delegation CLI)
  presets/      builtin sandbox and command presets (TOML)
mod/            C# RimWorld mod (Harmony, 1.6)
  Source/       mod source
  Assemblies/   compiled DLL and Harmony
  Tests/        game-free C# tests
  Defs/         XML defs
  Textures/     baked icons and faceplates
  Sounds/       soundtrack OGGs
docs/           mdBook documentation
  src/          Markdown source
  book/         build output (gitignored)
tools/          Python and shell build tools
notes/          developer notes (not user-facing)
```

## Development workflow

All targets go through the Makefile. `make` prints the full list.

```sh
RIMWORLD=/path/to/RimWorld/game make all      # build both halves
make test                                      # cargo test + C# tests + prose linter
make format                                    # format both halves
make lint                                      # lint both halves
make install                                   # install daemon, runner, mod
make run                                       # launch through the runner
```

Set `BUILD=release` for release builds. See [Build from source](../build.md) for
the full reference.

## Checks

`make test` runs Rust unit tests, game-free C# tests under `mod/Tests/`, and the
prose linter's own tests.

`make lint` checks Rust formatting and clippy warnings (warnings as errors), then
rebuilds the mod in Release with warnings as errors and verifies C# formatting.

`make lint-prose` scans Markdown and source comments for LLM clichés. Errors fail the
check; density and vocabulary warnings are advisory unless `--fail-on-warnings` is
passed.

`make coverage` produces Cobertura XML reports for both halves; it requires
`cargo-llvm-cov` and the matching LLVM tools.

## Generated assets

Several `tools/` scripts produce committed output:

| Script | Target | Output |
| --- | --- | --- |
| `tools/appicon.py` | `make appicon` | App icon |
| `tools/icons.py` | `make icons` | Action icons from Nerd Font Codicons |
| `tools/roboface.py` | (manual) | Agent faceplates |
| `tools/fileicons.py` | (manual) | File-sidebar icons |
| `tools/emoji_atlas.py` | `make emoji-atlas` | Supplementary-plane emoji atlas |
| `tools/reference.py` | `make reference` | Environment/API/CLI reference |

## Devnotes

The `notes/` directory holds short developer notes, one subject per file. Notes record
cross-file architecture, non-obvious constraints, and operational facts that the code
or git history does not show. [notes/index.md](../../notes/index.md) is the index.

Notes are internal. When a note disagrees with a published doc page, the note is wrong.
See [notes/prose-guide.md](../../notes/prose-guide.md) for writing rules and
[notes/house-rules.md](../../notes/house-rules.md) for commit policy.

## Commit policy

All work lands on `main`. The repository has one author and a linear history. Branches
are used only for work that will be merged back.

## Documentation

`make docs` builds the mdBook; `make docs-serve` serves it locally. Build output
under `docs/book/` is gitignored.
