# Terminal rendering

`TerminalRunCache` owns cached ANSI rows and link decoration.
The panel owns rendered text.
Cursor/selection overlays
must not force text repaint. Row damage describes one received revision: if painting skipped
that predecessor, use a full repaint rather than applying incomplete damage.

Parsed runs contain resolved colors. Theme revision must invalidate both parsed and rendered
caches.
Match UI follows the UI scheme. Explicit terminal themes do not. Font atlas and UI
scale changes also affect cell geometry and cached textures. Entering or leaving an
alternate-screen app such as `less` invalidates the pixel cache even when the visible rows and
content revision happen to be unchanged.

The daemon owns cell geometry. Its CHA markers close wide glyphs even at the trimmed row
end.
Parsed runs retain that occupied width. They must not merge past a wide glyph. Never
reconstruct widths from Unicode ranges or font metrics. Selection and copy share glyph
boundaries.
The cache keeps complete scalar strings separate from continuation cells.

`UI/Text/InlineTextLayout` positions both font spans and catalog sprites.
`SharedTextRenderer` controls their clipped drawing for terminal runs and UI labels. Terminal layout consumes daemon
columns, while UI layout measures plain spans and reserves a line-height box for sprites.
The generated catalog matches text keys, including baked sequences, without width tables.
Catalog glyphs never reach Unity's font loader.
Missing artwork occupies the same measured box with a replacement character. Unknown supplementary text reaches font fallback as complete
scalars. Build details belong in [tools](build-tools.md).

Autolinks may span physical rows and color runs. Explicit OSC 8 targets take precedence.
A blank tail ends continuation. Compare link spans against the last parse, not the last received frame:
multiple updates can arrive before a draw. Parsed rows and span metadata stay immutable
when shared with snapshots. Link scans write column-indexed characters directly into
reused storage.
Do not create selection/copy cell strings on this frequently used path.

File-link activation is deliberately lazy: recognize on Ctrl+left-click, then open a dedicated file menu.
Resolve paths against the terminal working directory.
View and Edit preserve diagnostic line numbers.
A daemon browse of the parent confirms the target type before offering file operations.
Directories omit View/Edit. Text files use the Files text policy. The “Choose an application”
submenu and File actions reuse the Files menus. Missing or unconfirmed targets offer only Copy path.
Right-click keeps pane actions separate.
Copy path remains available outside a project.
Only paths inside the session project support reveal actions.
Hover and repaint must not start filesystem work. URL and file-link behavior share terminal input ownership, not sidebar selection state.
