# UI icon ownership

`UI/Theme/Icons.cs` exposes semantic slots; `tools/icons/manifest.toml` maps them to
Codicons glyph names and codepoints. Generated PNGs live in
`mod/Textures/SlopWorld/Icons/`. Update the manifest and slot table together.

Committed PNGs keep normal builds independent of local fonts. The mapping is stable,
but bake output depends on the selected Nerd Font and rasterizer. A common scale
preserves relative glyph sizes; do not fit every glyph independently.
Run `just bake-icons` to regenerate action icons. The baker defaults to 64 px;
`python3 tools/icons.py` accepts `--size`, `--font`, and `--report` for glyph metrics.
The selected Nerd Font is a build input and is not shipped.

`Icons.Get` caches `BadTex` when content lookup returns null. File icon lookup is a
separate catalog; general texture lifetime traps
belong to [Harmony gotchas](core-gotchas.md).

`UI/Browsing/FileIcons.cs` owns file/tree artwork from vendored Material Icon Theme
assets and caches fallbacks for null/BadTex misses. The manifest and C# lookup change
together; filename matches precede the longest extension match.
