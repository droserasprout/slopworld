# Icons

Action icons are 64px PNGs baked from VS Code [Codicons][c] by `tools/icons.py` and
loaded through `UI/Theme/Icons.cs`. The slot names are semantic (`Icons.Agents`, not
`Icons.Robot`) so artwork can change without changing call sites.

[c]: https://github.com/microsoft/vscode-codicons

`tools/icons/manifest.toml` maps slots to glyph names and codepoints.
The generated files are in `mod/Textures/SlopWorld/Icons/`. Update the manifest and `UI/Theme/Icons.cs` together manually.
The mod's flat settings parser does not load this catalog.
Run `python3 tools/icons.py`.
The `--report` option prints glyph size and ink coverage.

The baker accepts any installed Nerd Font through FreeType. The font is not shipped:
committed PNGs make builds independent of the local font, while the manifest's
codepoints keep baking reproducible. One common scale and centred output preserve
Codicons' relative weights.
Do not fit each glyph separately.

`ContentFinder` returns `BaseContent.BadTex` rather than null on a miss. Runtime
textures that must remain through map changes need `HideFlags.DontUnloadUnusedAsset`.
The content tables retain loose icon textures. Code-generated textures remain
necessary for the hardware `DeadCursor`, robot faces,
and the menu background.

Notable aliases: `gear` serves options/config, `eye` serves hidden/view, `debug-stop`
is the stop icon, `circle-filled` is the state dot, `symbol-event` is the Library icon,
`text-size` is type, `credit-card` is usage, and `link` is Integrations. `RobotFace_south` is a pawn
faceplate, not a sidebar icon.
`UI/Browsing/FileIcons.cs` owns the shared file/tree lookup, including cached
fallbacks for null and `BadTex` misses. It uses a separate set of images generated from Material Icon Theme.
