# Icons

Action icons are 64px PNGs baked from VS Code [Codicons][c] by `tools/icons.py` and
loaded through `UI/Icons.cs`. The 27 slot names are semantic (`Icons.Agents`, not
`Icons.Robot`) so artwork can change without changing call sites.

[c]: https://github.com/microsoft/vscode-codicons

`tools/icons/manifest.toml` maps slots to glyph names and codepoints; the generated
files live in `mod/Textures/SlopWorld/Icons/`. The manifest and `UI/Icons.cs` are
kept in sync by hand because the mod has no TOML parser. Run `make icons`; `--report`
prints glyph size and ink coverage.

The baker accepts any installed Nerd Font through FreeType. The font is not shipped:
committed PNGs make builds independent of the local font, while the manifest's
codepoints keep baking reproducible. One common scale and centred output preserve
Codicons' relative weights; do not fit each glyph separately.

`ContentFinder` returns `BaseContent.BadTex` rather than null on a miss. Runtime
textures that need to survive map changes must use `HideFlags.DontUnloadUnusedAsset`;
loose icon textures are rooted by the content tables. Code-generated textures remain
necessary for pixel-snapped `Slab` corners, the hardware `DeadCursor`, robot faces,
and the menu background.

Notable aliases: `gear` serves options/config, `eye` serves hidden/view, `debug-stop`
is the stop icon, `circle-filled` is the state dot, `symbol-event` is the Library icon,
`text-size` is type, and `credit-card` is usage. `RobotFace_south` is a pawn
faceplate, not a sidebar icon; `FileIcons` is a separate Material Icon Theme bake.
