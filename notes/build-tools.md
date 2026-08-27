# Build tools and formatting

The build targets and install workflows are in [build-commands](build-commands.md).

## Formatting

For C#, run `dotnet format` in folder mode with whitespace-only changes;
`.editorconfig` preserves single-line statements. It needs SDK reference
assemblies, while the mod compiler uses direct Mono `csc`: `format-mod` stops without
an SDK, and `lint-mod` still runs compiler warnings. Override `CSC` or `CSC_API` when
the compiler or Mono reference assemblies live elsewhere.

## Auxiliary tools

None of these tools run as part of a build:

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
  Codicons; wants one installed, unlike the others - see [mod-icons](mod-icons.md).
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
