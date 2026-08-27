# The terminal pane

`TerminalWindow` renders the pane and forwards keys. Escape goes to the agent, so
Shift+Escape leaves; F12 closes; Alt+1..9/Alt+0 selects a portrait through
`AgentColony.InBarOrder`. `TerminalHotkeys` handles the same numbers on the map because
game components run before the window stack in `UIRootOnGUI`. `SnapX`/`SnapY` put edges
on screen pixels.

The window uses `Margin` 0 so GUI-group and screen coordinates agree
([gotchas](gotchas.md)).

## Size

`NegotiateSize` divides the body by cell size and sends a debounced `resize` (0.2s).
It retries once per second while returned frames disagree, which is needed because a
socket can drop during redeploy. `session.rs`'s `BOOT_COLS`/`BOOT_ROWS` are the initial
size.
The measured font advance is snapped to screen pixels after `Prefs.UIScale`; rendering,
cursor geometry, hit-testing and resize negotiation all use that same snapped cell.

## Title bar

The title bar has gear and close buttons; stopped agents selected from terminal mode stay
visible without being started, and their Start/Edit actions are drawn over the pane.
Buttons use the icon bake ([mod-icons](mod-icons.md)) and draw before
`ColonistBarStrip.Draw`, so the strip reserves `TerminalWindow.CornerW` at both ends.
`OpenMenu` is handled before key forwarding in every mode, allowing a menu from a
full-screen TUI. The second line is `ScreenView.title` from OSC 0/2, only when non-empty.
When the focused process exits (including Ctrl+C/Ctrl+D or a host shell's Ctrl+D), the pane
advances to the next live session; it closes only when there is no live session left.

The colonist strip is part of the title bar, so `HeaderH` is `ColonistBarStrip.BarH`.
`OpenOverPane` puts a bar-opened window on the same Super layer as the pane. In sidebar
layout the window draws no header: `TopBar` owns the name, state and buttons.

## Keys

`MapKey` uses tmux names (`C-Left`, `M-Up`, `S-Right`). Shift is sent only on the alt
screen: editors use shifted arrows and page keys, while zsh/bash treat those sequences as
undefined ([zsh-terminal](zsh-terminal.md)). On the primary screen, Shift+PgUp/PgDn move
the mod's own scrollback by one viewport.

These combos are hardcoded rather than `KeyBindingDef`s, so `KeyBindingsPage` does not
list them and they cannot be rebound: Alt+Z/Alt+X and Alt+comma/Alt+period walk the terminal
tab list (including host and ephemeral tabs),
Shift+Enter sends `\e[13;2u` so an agent inserts a newline instead of submitting, Ctrl+C
copies when text is selected and otherwise falls through as SIGINT, Ctrl+V pastes, and
middle-click pastes the host's Wayland/X11 PRIMARY selection (even when an app reports
mouse input). Agent panes use the normal clipboard read so image pastes remain available;
host panes use a text-only read.
Shift+F1..F12 forwards the F-key to the agent while a bare F-key is the mod's.

Unity can lose the semicolon IMGUI event. The terminal therefore checks both the
character stream and named key, deduplicated per frame; Shift suppresses the `;` fallback
when the physical key produced `:`. A consumed event is also checked by `rawType`.
Semicolon uses byte-preserving paste after flushing ordinary text, rather than tmux's
named-key path.

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
