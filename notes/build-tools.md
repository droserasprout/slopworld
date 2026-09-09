# Build tools and formatting

The build targets and install workflows are in [build-commands](build-commands.md).

Formatting and prose-linter usage live in [Build from source](../docs/src/build.md).
The prose linter skips Markdown code fences and inline code; its `--commit-msg`
mode also ignores Git template comments and verbose diffs.

## Auxiliary tools

- `make scheme-report` - measures the three house UI schemes and One Dark, including alpha
  compositing, and checks that Warm stays within 5% of Cold's luminance/contrast hierarchy.
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
  C# code table for the terminal renderer (`make emoji-atlas`).
- `tools/split_ost.py` - crops the newest Bitwig FLAC export at the fixed OST
  boundaries into 192 kbps OGGs in `.ost-staging/`.
- `tools/install_ost.py` - copies the newest staged dated tracks into
  `mod/Sounds/SlopWorld/OST/` and updates `Defs/Songs.xml`; `Radio.cs` points the daemon at
  that directory.
