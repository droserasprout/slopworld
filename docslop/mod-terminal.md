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

## Title bar

The title bar has gear and close buttons; agent-ending actions remain in the agents list.
Buttons use the icon bake ([mod-icons](mod-icons.md)) and draw before
`ColonistBarStrip.Draw`, so the strip reserves `TerminalWindow.CornerW` at both ends.
`OpenMenu` is handled before key forwarding in every mode, allowing a menu from a
full-screen TUI. The second line is `ScreenView.title` from OSC 0/2, only when non-empty.

The colonist strip is part of the title bar, so `HeaderH` is `ColonistBarStrip.BarH`.
`OpenOverPane` puts a bar-opened window on the same Super layer as the pane. In sidebar
layout the window draws no header: `TopBar` owns the name, state and buttons.

## Keys

`MapKey` uses tmux names (`C-Left`, `M-Up`, `S-Right`). Shift is sent only on the alt
screen: editors use shifted arrows, while zsh/bash treat those sequences as undefined
([zsh-terminal](zsh-terminal.md)).

These combos are hardcoded rather than `KeyBindingDef`s, so `KeyBindingsPage` does not
list them and they cannot be rebound: Alt+comma/Alt+period walk the session list,
Shift+Enter sends `\e[13;2u` so an agent inserts a newline instead of submitting, Ctrl+C
copies when text is selected and otherwise falls through as SIGINT, Ctrl+V pastes, and
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

The picker contains the only original palette, `SlopWorld`, plus named classic palettes:
One Dark, Dracula, GNOME light/dark, Tango light/dark, Gruvbox, Nord, Solarized light/dark,
Monokai and VS Code Dark+. `Name` is the persisted ID and `Label` is the visible picker
name. The old `slate` and `paper` entries are retired; their saved values use the normal
unknown-theme fallback to SlopWorld.

The 16-color entries follow the published palettes where one exists. GNOME and Tango use
the palettes shipped by GNOME Terminal; the UI's additional surfaces and cursor/link roles
are small adaptations required by this pane rather than claims that those source projects
define those exact roles.

## Links

- `emu.rs` preserves application OSC 8 after `safe_uri` strips controls and caps length.
- `Sgr.Autolink` scans screen rows as fixed-width characters for plain URLs, then splits runs;
  existing links win. A URL that reaches a row edge continues onto the next row, while a blank
  tail breaks it.
- `TrackHover`/`LinkAt` share lookup for highlight, tooltip and click. Ctrl+click uses
  `POST /api/open`; `open.rs` permits only http/https/mailto and tries `xdg-open`, `gio`
  and `wslview`, with `Application.OpenURL` as fallback.
