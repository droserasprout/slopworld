# Icons

Every action icon the mod draws is one PNG, baked from [Codicons][c] — VS Code's
icon set — by `tools/icons.py` and looked up through `UI/Icons.cs`. All twenty-three
slots come from that one set.

[c]: https://github.com/microsoft/vscode-codicons

## Where things are

- `tools/icons/manifest.toml` — slot → glyph, one table per slot, with a `note`
  where the choice is not the obvious one.
- `mod/Textures/SlopWorld/Icons/<slot>.png` — the bake's output, 64px, ~84K.
- `UI/Icons.cs` — the lookup. The manifest and this file are kept in step **by
  hand**: shipping the manifest into the game would mean a TOML parser the mod
  has not got. Same arrangement as `FileIcons` and its manifest.

`make icons` rebakes. `--report` prints each glyph's drawn size and ink coverage.

## The font, and why it is not vendored

The glyphs come out of a **Nerd Font**, which is not itself an icon set — it is a
font patched to carry other people's sets in the private use area, Codicons among
them. `tools/icons.py` wants any Nerd Font (Arch: one of the `ttf-*-nerd`
packages) and rasterizes through FreeType, which pillow already carries, so unlike
`tools/fileicons.py` it needs no `rsvg-convert`.

The font is not vendored the way the fileicons SVGs are: it is four megabytes to
hold twenty-three glyphs. The PNGs are committed instead, so a build never needs the
font and only a rebake does. The manifest records both the Nerd Fonts glyph name
and its codepoint — the name is documentation and the codepoint is what the bake
reads — so nothing fetches `glyphnames.json` at bake time. Both are on the [cheat
sheet](https://www.nerdfonts.com/cheat-sheet).

## One scale, not twenty-three

Codicons is drawn on a 16px grid for a code editor: sharp corners, no round caps,
one weight throughout. That is the whole reason to take it, and it is what the two
tries before this one did not have — an emoji bake has no common grid at all, and
Feather's round caps and round joins read as too soft for a terminal.

Its glyphs are **not** all the same size and are not meant to be: `circle-filled`
is an inline status dot at 129/256 em where `terminal` fills its cell at 224. So
the bake scales every glyph by ONE factor, the one that fits the largest of them,
and centres each in the square. Fitting each glyph to the box separately would
flatten exactly the relative sizing the set was drawn with — and per-glyph fitting
by eye is what the nine procedural icon classes this replaced had to do, the old
`TabIcons` holding each shape to a 22×22 box *and* to ~250 of 1024 covered pixels
because five of them had to agree about what "the same weight" meant.

Ink lands between 5.9% (`check`) and 38.1% (`trophy`, the only solid glyph);
`--report` prints it, because that agreement is the reason to use a real set.

## Why a set and not drawn shapes

The reason the shapes were drawn was that vanilla keeps its art in asset bundles,
so a content path like `TexButton`'s resolves to null and draws a button nobody
can see. That is true of **vanilla** paths. The mod's own `Textures` tree is loose
and `ContentFinder` reads it, which `FileIcons` had been proving the whole time.

## The loader

- A miss is `BaseContent.BadTex`, never null. A file-type icon that fails to load
  is a blank cell; a gizmo icon that fails to load is an invisible button, which is
  the exact trap the drawn icons existed to avoid.
- No `HideFlags.DontUnloadUnusedAsset` here. `ContentFinder`'s textures are rooted
  by the content tables; it is the ones built at runtime — `Slab`'s corners,
  `DeadCursor` — that the unload on a map switch would take.

Slots are named for the slot, not the glyph: `Icons.Agents`, not `Icons.Robot`. The
agents tab is the agents tab whatever picture it wears next year.

## What is still drawn in code

- `UI/Slab.cs` — the rounded-rect corners must match the screen-pixel grid at the
  current UI scale and are rebuilt when it changes. Cannot be prebaked.
- `UI/DeadCursor.cs` — the hardware cursor, which reads back and spins.
- `Sim/RobotFace.cs` — pawn render-node textures, a different concern entirely.
- `UI/MenuBackground.cs` — full-frame effects.

## Notes on particular slots

- **agents** is `robot`, an actual one — Codicons has it where Feather and Ionicons
  both did not. `RobotFace_south` is a pawn's faceplate: coloured, with eye
  variants, and a blob when tinted flat at 18px.
- **gear** serves both the options row and the top bar's config door; `Icons.Config`
  is an alias, because the two call sites are not about the same thing.
- **eye** likewise serves `Icons.Hidden` (the dotfile switch) and `Icons.View` (a
  row's view action).
- **stop** is `debug-stop`, not `primitive-square`: the latter is Codicons' small
  inline marker and reads as a pebble beside `play`.
- **dot** is `circle-filled` and *is* that small inline marker, which is what the
  pager's mark is.
- **shortcuts** is `symbol-event`, which Codicons draws as the lightning bolt. The
  set the Nerd Font bundles has no `zap`.
- **keyboard** is `keyboard`, on the options column's Keyboard row. It wore the
  bolt above under the label "Shortcuts", which is the sidebar's errands — one
  word and one picture doing duty for two unrelated things.
- **type** is `text-size`, the appearance row's Aa.
- **usage** is `credit-card` on the Usage row, which used to draw a lump of
  `ThingDefOf.Silver` — the game's money on a page about what an API bills, and the
  one row in the column not from the set. The readouts on the page itself keep the
  silver: drawing spend as a resource is the joke there.
- **trophy** is `star-full` on the About row; Codicons has no trophy and a star is
  the same joke at 20px.
- **add** is `add`, the sidebar's one add button. A glyph rather than a `"+"` in
  `GameFont.Medium`, which was as large as vanilla's fonts go and still a hairline
  in a 26px strip.
- **diff** is `git-compare`, native to the set. `FileIcons` has its own unrelated
  `diff.png` for `.diff`/`.patch` files, off the Material Icon Theme bake — the two
  do not share.
