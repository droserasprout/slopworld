# Loading screen

`Patches/LoadingScreen/` owns the loading-time tip stream and Harmony drawing hooks.
The stream measures fixed-width cells and draws the committed ASCII atlas. Loading
text size follows the terminal font preference rather than an independent setting.

Measure the panel before reading geometry. Repaint reflows a resized retained stream
when no new words are due. The custom loading-screen owner handles both layout and
drawing suppression of vanilla panels. Tips can draw during map generation, so their
random selection must not consume deterministic gameplay RNG.

Tips use printable ASCII U+0020–U+007E with newline as an explicit paragraph separator.
Unsupported glyphs fall back to `?`. Glyphs share a baseline and transparent gutters;
keep the runtime line-height constant aligned with the baker's metric check.
Run `make bake-loading-font` to regenerate the atlas from `assets/fonts/clacon2.ttf`.
`make test-text-sprites` checks generated text-sprite metadata and rejects non-ASCII
loading-tip literals; it does not reject every ASCII control character.
