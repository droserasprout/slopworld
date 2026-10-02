# Terminal rendering

`TerminalRenderer` coordinates painting; `TerminalPanel.Cache` owns panel textures
and repaint lifetime. `TerminalRunCache` and `Sgr` own parsed rows; `TerminalTheme`
and `TerminalFont` supply cache revisions. Parsed link decoration belongs to
`TerminalAutolinks`, with URL recognition in `UrlScan`.

Texture creation failure falls back to direct painting. Damage describes one received
revision: skipped predecessors or missing damage require a full repaint. Cursor and
selection overlays must not repaint text. Theme/font, geometry, history position,
and screen-mode changes invalidate the appropriate parsed/rendered caches.

Daemon columns own glyph widths; never reconstruct them from Unicode ranges or font
metrics. Complete scalars and occupied widths stay intact through parsing, sprite
joining, selection, and copy. Emoji clusters remain separate from adjacent text so
whole-key atlas lookup works. [Wire protocol](protocol-wire.md) owns cell encoding,
and [shared text](mod-ui-text.md) owns clipped font/sprite layout and fallback.

Input/link activation belongs to [terminal](mod-terminal.md), daemon widths and
capture to [capture](daemon-terminal-capture.md), asset regeneration to
[shared text](mod-ui-text.md), and profiling
to [latency diagnostics](terminal-latency.md).
