# Loading screen

`Patches/LoadingScreen/` owns the loading-time tip stream and Harmony drawing hooks.
The stream measures fixed-width cells and draws the committed ASCII atlas. Loading
text size follows the terminal font preference rather than an independent setting.
The stream draws directly over the loading artwork, with no panel background or shadow.

Measure the panel before reading geometry. Repaint reflows a resized retained stream
when no new words are due. The custom loading-screen owner handles both layout and
drawing suppression of vanilla panels. Tips can draw during map generation, so their
random selection must not consume deterministic gameplay RNG.

At each quote boundary, selection ranks allowed quotes by how closely their first
words fill the remaining monospace cells, including the separating space. Equal
fits are randomized, and the last twelve quotes are excluded. The stream owns
geometry and mode filtering; `LoadingTipSelection` owns ranking and recent history,
which resets with a new load or mode change. Quotes continue across word wraps.

Tips use printable ASCII U+0020–U+007E with newline as an explicit paragraph separator.
Unsupported glyphs fall back to `?`. Glyphs share a baseline and transparent gutters;
keep the runtime line-height constant aligned with the baker's metric check.
Run `just generate-loading-font` to regenerate the atlas from `assets/fonts/clacon2.ttf`.
`just tools-test tools/assets/test_text_sprites.py` checks generated text-sprite metadata and rejects non-ASCII
loading-tip literals; it does not reject every ASCII control character.
