# Loading screen

`Patches/LoadingScreen/` owns tip streaming and vanilla drawing suppression. It uses
the committed ASCII atlas and terminal font preference. Measure before reading
geometry, and reflow retained text on resize even when no words are due.
The full-height stream occupies the rightmost 38% of the screen, fitting the final
background frames' black area with space beside the planet. Keep its width
proportional rather than capped in UI units so UI scale does not change its coverage.
Streaming uses 75 words per second at 2x UI scale; the rate and per-frame allowance
scale with inverse UI scale squared to account for the larger text capacity at lower scales.
Overflow scrolls in two half-line steps with a brief hold between them. Retain the
outgoing row through the first step, and finish the second before appending more
text. Reset the scroll offset on reflow or a fresh stream.

Tip selection must not consume gameplay RNG because it runs during map generation.
`LoadingTipSelection` owns fit ranking and recent history; the stream owns geometry
and mode filtering. Reset selection history on a new load or mode change. Quote
markers control Grandma eligibility and are stripped before display.

Keep tips printable ASCII with newline as a paragraph separator. The glyph baseline,
gutters, and runtime line height must agree with the baker. Regenerate with
`just generate-loading-font`; [asset tests](../tools/assets/test_text_sprites.py)
check the generated metadata and non-ASCII tip literals.
