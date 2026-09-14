# Terminal rendering

`TerminalRunCache` owns parsed rows; the panel owns rendered text. Cursor/selection overlays
must not force text repaint. Row damage describes one received revision: if painting skipped
that predecessor, use a full repaint rather than applying incomplete damage.

Parsed runs contain resolved colors. Theme revision must invalidate both parsed and rendered
caches; Match UI follows the UI scheme, explicit terminal themes do not. Font atlas and UI
scale changes also affect cell geometry and cached textures.

Unity's font path cannot reliably read Noto Color Emoji bitmap tables. The Pango-baked atlas
handles supplementary-plane glyphs first; requesting only half a surrogate pair from the
fallback font is invalid. Build details belong in [tools](build-tools.md).

Autolinks may span physical rows and color runs. Explicit OSC 8 targets win; a blank tail
ends continuation. Incremental parsing must invalidate connected spans, including removed
links, without mutating retained snapshots. See the [CPU plan](plan-cpu-fixes.md).

File-link activation is deliberately lazy: recognize on click, then reveal through Files.
Only paths inside the session project are revealable; hover/repaint must not start filesystem
work. URL and file-link behavior share terminal input ownership, not sidebar selection state.
