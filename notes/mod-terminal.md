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
the mod's own scrollback by one viewport. Mouse-wheel scrollback uses `SmoothScroll`'s
fractional local position. `TerminalHistory` indexes the overlapping rows in daemon viewport
snapshots by their offset and assembles a local view with one overscan row; requests prefetch
half a viewport in the gesture direction, so skipped integer offsets remain local. The daemon's
10,000-line history limit is the initial coordinate range; reaching the real top clamps it to
the offset the daemon achieved. While scrolled back, a three-unit overlay bar at the pane's
right edge shows the current offset against the daemon-reported history extent without changing
the negotiated terminal width.

These combos are hardcoded rather than `KeyBindingDef`s, so `KeyBindingsPage` does not
list them and they cannot be rebound: Alt+Z/Alt+X and Alt+comma/Alt+period walk the terminal
tab list (including host and ephemeral tabs),
Shift+Enter sends `\e[13;2u` so an agent inserts a newline instead of submitting, Ctrl+C
copies when text is selected and otherwise falls through as SIGINT, Ctrl+V pastes, triple-click
publishes its line to the host's Wayland/X11 PRIMARY selection, and middle-click pastes that
selection (even when an app reports mouse input). Non-Codex agent panes use the normal
clipboard read; host panes use a text-only read. Codex panes probe the text-only clipboard first,
because Codex's image-paste handler
otherwise reports a missing image for ordinary text; they forward Ctrl+V only for image (or
other non-text) clipboard data. The built-in Codex sandbox therefore includes the X11 and
Wayland display capabilities it needs.
Shift+F1..F12 forwards the F-key to the agent while a bare F-key is the mod's.

Unity can lose the semicolon IMGUI event. The terminal therefore checks both the
character stream and named key, deduplicated per frame; Shift suppresses the `;` fallback
when the physical key produced `:`. A consumed event is also checked by `rawType`.
Semicolon uses byte-preserving paste after flushing ordinary text, rather than tmux's
named-key path.

Theme, font, selection, URL, and file-link behavior are covered in
[terminal rendering and links](mod-terminal-rendering.md).
