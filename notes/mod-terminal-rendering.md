# Terminal rendering

`TerminalRenderer` coordinates painting; `TerminalPanel.Cache` owns panel textures
and repaint lifetime. `TerminalRunCache` and `Sgr` own parsed rows; `TerminalTheme`
and `TerminalFont` supply cache revisions. Parsed link decoration belongs to
`TerminalAutolinks`, with URL recognition in `UrlScan`. The bounded parsed-row cache
uses FIFO eviction; retained live frames reuse link-candidate transitions per row. Candidate scratch
belongs to the parser, not shared frame snapshots; transitions preserve delimiters
split across physical rows.

`TerminalFontFallbacks` expands family priorities from the installed font catalog
owned by `InstalledFonts`. Catalog revisions invalidate the active terminal font,
monospace probes, and cached font menus; font invalidation also advances the terminal
render revision. Nerd Font variants precede their original within each family;
fixed-width icon variants take precedence over regular Nerd Fonts. Renamed
families use the shared `FontFamilyIdentity` stem mappings rather than separate
hardcoded variant lists.

Texture creation failure falls back to direct painting. Damage describes one received
revision: skipped predecessors or missing damage require a full repaint. Cursor and
selection overlays must not repaint text. Theme/font, geometry, history position,
and screen-mode changes invalidate the appropriate parsed/rendered caches.

On backends with texture-copy support, each panel retains a physical-pixel crop.
Painting uses a pooled, temporary screen-sized target so IMGUI clipping and glyph
sampling keep their existing screen coordinates; this scratch contributes to peak
GPU storage. Backends without copy support retain the screen-sized cache. History
scrolls reuse matching, link-free rows only at integer physical-pixel shifts;
exposed and boundary rows repaint. Scale, origin, surface or content mismatches
fall back to a full repaint. Cache copy coordinates follow the same backend Y
orientation as texture sampling.

Selection painting retains byte geometry under weak immutable-row keys. Replacement
rows invalidate that geometry without retaining sessions or games. Copying text
still uses complete scalar strings; selection geometry preserves the same daemon
continuation cells, overlaps and trailing-space rules.

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
