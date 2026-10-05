# UI icon ownership

`UI/Theme/Icons.cs` exposes semantic slots; `assets/icons/manifest.toml` maps them to
Codicons glyph names and codepoints. Generated PNGs live in
`mod/Textures/SlopWorld/Icons/`. Update the manifest and slot table together.

Committed PNGs keep normal builds independent of local fonts. The baker defaults to
the pinned Symbols Nerd Font in `assets/fonts/nerd-symbols/`; `source.toml` records
its version, source, and checksums. Update upstream notices and glyph licenses in `licenses/nerd-fonts/`
when updating the font. Bake output also depends on the rasterizer. A common scale
preserves relative glyph sizes; do not fit every glyph independently.
Run `just refresh-icons` to regenerate action icons. The baker defaults to 64 px;
`uv run --locked --extra assets python -m tools.assets.icons` accepts `--size`, `--font`, and `--report` for glyph metrics.
The font is a build input; the mod ships only the generated PNGs.

`Icons.Get` caches `BadTex` when content lookup returns null. File icon lookup is a
separate catalog; general texture lifetime traps
belong to [Harmony gotchas](core-gotchas.md).

`UI/Browsing/FileIcons.cs` owns file/tree artwork from vendored Material Icon Theme
assets and caches fallbacks for null/BadTex misses. The manifest and C# lookup change
together; filename matches precede the longest extension match.
`assets/fileicons/` owns the vendored SVGs and manifest; its license is in
`licenses/material-icon-theme/`;
`tools/assets/fileicons.py` bakes them into `mod/Textures/SlopWorld/FileIcons/`.
