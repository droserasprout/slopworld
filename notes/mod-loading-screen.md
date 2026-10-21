# Loading screen

`Patches/LoadingScreen/` owns tip streaming and vanilla drawing suppression. It uses
the committed ASCII atlas and terminal font preference. Measure before reading
geometry, and reflow retained text on resize even when no words are due.

Tip selection must not consume gameplay RNG because it runs during map generation.
`LoadingTipSelection` owns fit ranking and recent history; the stream owns geometry
and mode filtering. Reset selection history on a new load or mode change. Quote
markers control Grandma eligibility and are stripped before display.

Keep tips printable ASCII with newline as a paragraph separator. The glyph baseline,
gutters, and runtime line height must agree with the baker. Regenerate with
`just generate-loading-font`; [asset tests](../tools/assets/test_text_sprites.py)
check the generated metadata and non-ASCII tip literals.
