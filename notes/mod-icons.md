# UI artwork

`UI/Theme/Icons.cs` exposes semantic slots; `assets/icons/manifest.toml` maps them to
pinned font glyphs or emoji. Update manifest and slots together. `UI/Browsing/FileIcons.cs`
uses a separate vendored file-icon catalog: filename matches precede longest-extension
matches. Both catalogs cache missing-texture fallbacks.

Normal builds consume committed PNGs. The icon font is a build input, not a shipped
runtime dependency. Its source/checksums and searchable cheatsheet live under
`assets/fonts/nerd-symbols/`; update notices in `licenses/nerd-fonts/` with the font.
Rasterizer changes also affect output. Preserve relative font-glyph sizes with a common
scale; emoji slots fit individually and publish white silhouettes.

`tools/assets/artwork.py` owns shared fitting and publication without rewriting equal
bytes. Originals/silhouettes share placement and alpha. `emoji_render.py` owns bundled
Noto/Pango rendering and premultiplied-to-straight RGBA conversion. [Shared text](mod-ui-text.md)
owns the text atlas and font selection.

The application icon and docs favicon share the in-memory robot/rose composition;
the favicon does not read a generated PNG. Use the `generate-appicon`,
`generate-favicon`, `generate-icons`, `generate-fileicons`, and `generate-emojis`
just recipes. `generate-all` includes these and the text atlas. Only textures that
must survive map changes need `DontUnloadUnusedAsset`.
