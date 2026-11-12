# Terminal rendering

`TerminalRenderer` coordinates painting; `TerminalPanel.Cache` owns panel textures
and repaint lifetime. `TerminalRunCache` and `Sgr` own parsed rows; `TerminalTheme`
and `TerminalFont` supply cache revisions. Parsed link decoration belongs to
`TerminalAutolinks`, with URL recognition in `UrlScan`. The bounded parsed-row cache
uses FIFO eviction; retained live frames reuse link-candidate transitions per row. Candidate scratch
belongs to the parser, not shared frame snapshots; transitions preserve delimiters
split across physical rows.
Panel closure releases its render texture; disposal also releases a cache created
before the panel opened. Repeated closure/disposal must remain harmless.

`Platform/Fonts/FontconfigFonts` obtains Linux fallback order from system Fontconfig
for the selected family (or `monospace` by default). It applies system substitutions
and returns monochrome families in `FcFontSort` order; color faces stay on the emoji
atlas path. `TerminalFontFallbacks` caches this order by selection and installed-font
revision, without promoting Nerd Fonts or special-casing glyphs. A failed query uses
the selected family plus `monospace` until a selection change or rescan retries it.
`InstalledFonts` publishes the font catalog; `NativeFonts` owns file discovery,
OpenType face selection, and native registration. A rescan invalidates both owners. Catalog revisions invalidate the active
terminal font, monospace probes, and cached font menus; font invalidation also advances
the terminal render revision. Fontconfig selects family order; the native backend resolves
and rasterizes those names.

`TerminalCachePaint` records full-repaint backgrounds and glyphs in the same native GUI
render-target batch. GPU clears through `GL.Clear` use a separate command stream; keep them
out of cache repaints. Content changes repaint the complete pane in one batch;
unchanged content and cursor-only updates retain the cache. Sparse row overpainting
is not used because it leaves stale scanlines under Waylith.

Cache sampling supplies bottom-origin UVs to `GUI.DrawTextureWithTexCoords`; the GUI backend
owns conversion to its texture origin. Do not add a graphics-API-dependent flip.

Texture creation failure falls back to direct painting. Damage describes one received
revision: skipped predecessors or missing damage require a full repaint. Cursor and
selection overlays must not repaint text. Theme/font, geometry, history position,
and screen-mode changes invalidate the appropriate parsed/rendered caches.

On backends with texture-copy support, each panel retains a physical-pixel crop.
Painting uses a pooled, temporary screen-sized target so IMGUI clipping and glyph
sampling keep their existing screen coordinates; this scratch contributes to peak
GPU storage. Close the scratch GUI draw target before copying its pixels: Waylith's
GPU copy does not flush pending GUI commands. Backends without copy support retain
the screen-sized cache. History scrolling repaints the complete pane, including
matching interior rows. Do not seed a partial GUI repaint with pixels copied through
a separate GPU command stream; a damaged interior can persist in a pinned view.
CopyTexture coordinates are bottom-origin even when
the backend reports top-origin UVs; sampling and copy conversion are separate contracts.

Selection painting retains byte geometry under weak immutable-row keys. Replacement
rows invalidate that geometry without retaining sessions or games. Copying text
still uses complete scalar strings; selection geometry preserves the same daemon
continuation cells, overlaps and trailing-space rules.

Terminal glyphs are positioned individually on the cell grid, including ASCII, so
scaled native font advances cannot accumulate drift. Daemon clusters remain intact.
The paint string uses canonical Unicode composition before font measurement and
drawing. Waylith paints scalar glyphs without mark attachment; composable accents
therefore use the same font-authored placement as precomposed text. Source clusters,
daemon widths and copied text remain unchanged. Noncomposable marks still depend
on native glyph placement.
Font painting clips to occupied cells and uniformly shrinks oversized fallback advances
by requesting a smaller font size, retaining the terminal baseline. It must not
change `GUI.matrix` per glyph: local coordinates inside nested IMGUI groups cannot
be used as screen-space transform pivots;
it must not borrow the following cell's drawing area. Supported BMP text-default
symbols use the terminal font after requesting their exact size/style and checking
loaded ink bounds; a coverage claim alone is insufficient. Missing or empty BMP glyphs and supplementary symbols use
monochrome packaged font ink. That ink inherits the terminal
foreground and selection geometry.
Color atlas glyphs fit their baked ink bounds into the occupied width and line
height, preserving aspect ratio. Monochrome fallback uses original font-relative ink
bounds and bearings at the configured font size, aligned to the terminal font's
baseline. It only shrinks uniformly when necessary and shifts overflow into its
cells. Atlas packing does not determine display size. Line height is a limit,
never a target that stretches every glyph to fill the row.
Transparent square tile padding must not force tall
one-column symbols down to a cell-width-sized square.
Daemon columns own glyph widths; never reconstruct them from Unicode ranges or font
metrics. Complete scalars and occupied widths stay intact through parsing, sprite
joining, selection, and copy. Emoji clusters remain separate from adjacent text so
whole-key atlas lookup works. [Wire protocol](protocol-wire.md) owns cell encoding,
and [shared text](mod-ui-text.md) owns clipped font/sprite layout and fallback.

Input/link activation belongs to [terminal](mod-terminal.md), daemon widths and
capture to [capture](daemon-terminal-capture.md), asset regeneration to
[shared text](mod-ui-text.md), and profiling
to [latency diagnostics](terminal-latency.md).

`Platform/TerminalRowCommands` is an optional drawing-only boundary around final
parsed/decorated rows. Native delegates remain null. The renderer owns ASCII
admission, placement/snap/theme/font identity and foreground exit state (empty rows
preserve incoming color). The browser owns operation capture, resource validation
and packet publication; it must never retain slices of mutable emitted batches.
Mod profiling bypasses this boundary so text/layout measurements retain their meaning.

Parsed wrapped-link decoration contributes to texture row damage, including
unchanged source rows and edits retained across a repeated frame.
