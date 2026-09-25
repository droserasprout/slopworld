# Loading screen

`Patches/LoadingScreen/` owns the loading-time tip stream and its Harmony drawing hooks.
The stream measures fixed-width cells and draws from the committed ASCII PNG atlas in
`Textures/SlopWorld/LoadingFont.png`. It draws at 80% of the configured loading font size to
match the previous IMGUI scale. `assets/fonts/clacon2.ttf` is only the source for
`make bake-loading-font`.

Keep tip strings within printable ASCII, the atlas's supported range. The game-free
`test-text-sprites` check rejects non-ASCII characters in tip literals while ignoring comments.
The baker places all glyphs on a shared baseline derived from font metrics, including glyph
overhangs, and leaves transparent gutters between cells to avoid atlas bleed. Keep the runtime
line-height constant in sync with the baker's metric check.
