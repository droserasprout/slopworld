# UI icon ownership

`tools/assets/appicon.py` owns the shared application artwork in
`mod/Textures/SlopWorld/SlopWorld_icon.png`. Desktop packages install it as the
128 px hicolor application icon. Run `just generate-appicon` to regenerate it.
`tools/assets/favicon.py` fits the same in-memory robot/rose composition for docs;
`just generate-favicon` does not consume generated PNG inputs.

`UI/Theme/Icons.cs` exposes semantic slots; `assets/icons/manifest.toml` maps them to
Codicons glyph names/codepoints or emoji sequences. Generated PNGs live in
`mod/Textures/SlopWorld/Icons/`. Update the manifest and slot table together.

Committed PNGs keep normal builds independent of local fonts. The baker defaults to
the pinned Symbols Nerd Font in `assets/fonts/nerd-symbols/`; `source.toml` records
its version, source, and checksums. The adjacent [Codicons cheatsheet](../assets/fonts/nerd-symbols/cheatsheet.tsv)
lists searchable glyph names and codepoints from that pinned upstream revision; refresh it
when updating the font. Update upstream notices and glyph licenses in `licenses/nerd-fonts/`
when updating the font. Bake output also depends on the rasterizer. A common scale
preserves relative Codicon sizes; do not fit every font glyph independently.
An optional manifest `scale` applies deliberate size adjustments to individual slots.
Emoji action slots are fitted individually and published as white silhouettes.
Run `just generate-icons` to regenerate action icons. The font is a build input;
the mod ships only the generated PNGs.

`Icons.Get` caches `BadTex` when content lookup returns null. File icon lookup is a
separate catalog; general texture lifetime traps
belong to [Harmony gotchas](core-gotchas.md).

`UI/Browsing/FileIcons.cs` owns file/tree artwork from vendored Material Icon Theme
assets and caches fallbacks for null/BadTex misses. The manifest and C# lookup change
together; filename matches precede the longest extension match.
`assets/fileicons/` owns the vendored SVGs and manifest; its license is in
`licenses/material-icon-theme/`;
`tools/assets/fileicons.py` bakes them into `mod/Textures/SlopWorld/FileIcons/`
through `just generate-fileicons`.

`tools/assets/emoji_render.py` owns Pango rendering and sequence-support probing from the bundled Noto font, and
conversion from Cairo premultiplied pixels to straight RGBA. `artwork.py` owns
shared fitting, white silhouettes, and PNG publication without rewriting identical
bytes. Catalogs own sizing policy. Originals and silhouettes share alpha and placement.
`assets/emoji/manifest.toml` owns standalone emoji outputs; regenerate them with
`just generate-emojis`. `just generate-all` includes these catalogs and the text atlas.
