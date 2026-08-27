# Terminal rendering and links

The terminal pane's sizing, title bar, and key routing are in
[mod-terminal](mod-terminal.md).

## `TerminalTheme`

`Sgr.DefaultFg`/`DefaultBg` resolve from the scheme. Parsed runs carry resolved colors,
so `Rev` keys both `ScreenBuf.RunsRev` and the pane render cache; idle panes otherwise
retain the old palette. Unknown names use the default. Cursor override accepts `#rrggbb`;
`CursorText` redraws the glyph over an opaque block cursor.

The picker contains the house palette plus named classic palettes. `Name` is the persisted
ID and `Label` is the visible picker name. The old `slate` and `paper` entries are retired;
their saved values use the normal unknown-theme fallback to SlopWorld.

The 16-color entries follow published palettes where one exists. Cursor, selection and link
roles are pane adaptations rather than claims that a source palette defines those roles.

`TerminalFont` keeps the selected mono face for the grid and adds installed emoji faces before
the broad symbol fallbacks. Legacy IMGUI cannot read Noto Color Emoji's bitmap tables reliably,
so `TerminalEmoji` draws supplementary-plane glyphs from the Pango-baked atlas before the font
path gets a chance. The generated atlas covers the codepoints advertised by the build machine's
Noto Color Emoji face; a rebuild is `make emoji-atlas`. The complete UTF-16 surrogate pair is
also requested from Unity's dynamic atlas for codepoints not in that atlas, and the pane cache
keys on font-atlas rebuilds.

Selection keeps a separate drag latch: a MouseDown/MouseUp in one cell is still a click, but a
real MouseDrag in that same cell selects and copies the single symbol.

## Links

- `emu.rs` preserves application OSC 8 after `safe_uri` strips controls and caps length.
- `Sgr.Autolink` scans screen rows as fixed-width characters for plain URLs, then splits runs;
  existing links win. A URL that reaches a row edge continues onto the next row, while a blank
  tail breaks it.
- `TrackHover`/`LinkAt` share lookup for highlight, tooltip and click. Ctrl+click uses
  Unity's `Application.OpenURL` directly.
- Ctrl+click on a file path reveals it in Files. `PathScan` accepts a relative path (`./x`,
  `../x`, or a token containing `/`) or an absolute one (`/x/y`); an absolute path is stripped
  against the session project's `Dir` and only revealed when it falls inside that root, since
  the tree lists no other. Recognition scans only the clicked row; Files lazily fetches its
  ancestors, so neither terminal repaint nor pointer hover pays for path navigation.
