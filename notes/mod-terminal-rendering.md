# Terminal rendering

`TerminalRunCache` owns cached ANSI rows and link decoration; the panel owns rendered text.
Cursor/selection overlays
must not force text repaint. Row damage describes one received revision: if painting skipped
that predecessor, use a full repaint rather than applying incomplete damage.

Parsed runs contain resolved colors. Theme revision must invalidate both parsed and rendered
caches; Match UI follows the UI scheme, explicit terminal themes do not. Font atlas and UI
scale changes also affect cell geometry and cached textures. Entering or leaving an
alternate-screen app such as `less` invalidates the pixel cache even when the visible rows and
content revision happen to be unchanged.

The daemon owns cell geometry. Its CHA markers close wide glyphs even at the trimmed row
end; parsed runs retain that occupied width, and must not merge past a wide glyph. Never
reconstruct widths from Unicode ranges or font metrics. Selection and copy share glyph
boundaries; complete scalar strings are kept separately from continuation cells.

`UI/Text/InlineTextLayout` positions both font spans and catalog sprites; `SharedTextRenderer`
owns their clipped drawing for terminal runs and UI labels. Terminal layout consumes daemon
columns, while UI layout measures plain spans and reserves a line-height box for sprites.
The generated catalog matches text keys, including baked sequences, without width tables.
Catalog glyphs never reach Unity's font loader; missing artwork occupies the same measured
box with a replacement character. Unknown supplementary text reaches font fallback as complete
scalars. Build details belong in [tools](build-tools.md).

Autolinks may span physical rows and color runs. Explicit OSC 8 targets win; a blank tail
ends continuation. Compare link spans against the last parse, not the last received frame:
multiple updates can arrive before a draw. Parsed rows and span metadata stay immutable
when shared with snapshots.

File-link activation is deliberately lazy: recognize on Ctrl+left-click, then open a dedicated file menu.
Resolve against the terminal cwd; View and Edit preserve diagnostic line numbers.
A daemon browse of the parent confirms the target type before offering file operations.
Directories omit View/Edit; text files use the Files text policy. Open in and File actions
reuse the Files menus. Missing or unconfirmed targets offer only Copy path.
Right-click keeps pane actions separate; Copy path remains available outside a project.
Only paths inside the session project are revealable; hover/repaint must not start filesystem
work. URL and file-link behavior share terminal input ownership, not sidebar selection state.
