# Terminal rendering

`TerminalRenderer` coordinates painting; `TerminalPanel.Cache` owns panel textures
and repaint lifetime. `TerminalRunCache` and `Sgr` own parsed rows; `TerminalTheme`
and `TerminalFont` supply cache revisions. Parsed link decoration belongs to
`TerminalAutolinks`, with URL recognition in `UrlScan`.
Panel closure releases its render texture; disposal also releases a cache created
before the panel opened. Repeated closure/disposal must remain harmless.

`TerminalCachePaint` records full-repaint backgrounds and glyphs in the same native GUI
render-target batch. GPU clears through `GL.Clear` use a separate command stream; keep them
out of cache repaints. Partial repaints clear only changed rows within the GUI batch.

Cache sampling supplies bottom-origin UVs to `GUI.DrawTextureWithTexCoords`; the GUI backend
owns conversion to its texture origin. Do not add a graphics-API-dependent flip.

Texture creation failure falls back to direct painting. Damage describes one received
revision: skipped predecessors or missing damage require a full repaint. Cursor and
selection overlays must not repaint text. Theme/font, geometry, history position,
and screen-mode changes invalidate the appropriate parsed/rendered caches.

Terminal glyphs are positioned individually on the cell grid, including ASCII, so
scaled native font advances cannot accumulate drift. Daemon clusters remain intact.
Daemon columns own glyph widths; never reconstruct them from Unicode ranges or font
metrics. Complete scalars and occupied widths stay intact through parsing, sprite
joining, selection, and copy. Emoji clusters remain separate from adjacent text so
whole-key atlas lookup works. [Wire protocol](protocol-wire.md) owns cell encoding,
and [shared text](mod-ui-text.md) owns clipped font/sprite layout and fallback.

Input/link activation belongs to [terminal](mod-terminal.md), daemon widths and
capture to [capture](daemon-terminal-capture.md), asset regeneration to
[shared text](mod-ui-text.md), and profiling
to [latency diagnostics](terminal-latency.md).

Parsed wrapped-link decoration contributes to texture row damage, including
unchanged source rows and edits retained across a repeated frame.
