# The terminal pane

`TerminalPanel` renders the pane and forwards keys. Escape goes to the agent, so
Shift+Escape leaves; F12 closes; Alt+1..9/Alt+0 selects a portrait through
`AgentColony.InBarOrder`. `TerminalHotkeys` handles the same numbers on the map because
game components run before the window stack in `UIRootOnGUI`. `SnapX`/`SnapY` put edges
on screen pixels.

The window uses `Margin` 0 so GUI-group and screen coordinates agree
([gotchas](core-gotchas.md)).

`TerminalWindow` is the fullscreen workspace host: chrome, placement, navigation and the
window-stack lifecycle. `TerminalPanel` owns `TerminalPanelState`, rendering and texture
caches, history, selection, clipboard, input controllers and resize scheduling. Its focused
partials live in `UI/Terminal/TerminalPanel/`. `ITerminalPanelHost` supplies workspace
navigation and input eligibility without giving the panel access to window internals.
`Opened`/`Closed` subscribe and release terminal resources; covering the panel only changes
visibility/focus. Pixel-snapping transforms are per panel. Input that closes or switches
the panel stops the old draw before it can rebuild a released cache.

`TerminalSplit` is the retained backing view for one or two terminals. The context menu's
`Open beside` selects a session for the other pane (replacing it when already split).
Opening an already-visible session focuses its existing pane; ordinary navigation replaces
only the focused pane. Click a pane to focus it; the lit top edge marks the selection.
Drag the divider to resize, bounded by the panes' minimum widths or equal available space
on a smaller viewport. `Close pane` and Shift+Escape remove the focused pane; F12 closes
the workspace. Settings covers both panes without releasing their state. Split placement
is not persisted, and Tab/Shift+Tab remain terminal input.
`WorkspaceSplit<T>` owns child lifecycle and focus; layout refreshes size each split pane
individually instead of broadcasting a shared grid. Render fallbacks and pointer overlays
are bounded to their pane while a resized screen is still in flight.

## Size

`NegotiateSize` divides the terminal panel's assigned rectangle by cell size and sends a debounced
`resize` (0.2s).
It retries once per second while returned frames disagree, which is needed because a
socket can drop during redeploy. `session/mod.rs`'s `BOOT_COLS`/`BOOT_ROWS` are the initial
size. There is no static last-used grid: initial sessions measure the host's target slot.
The measured font advance is snapped to screen pixels after `Prefs.UIScale`; rendering,
cursor geometry, hit-testing and resize negotiation all use that same snapped cell.

## Title bar

The title bar has gear and close buttons; stopped agents selected from terminal mode stay
visible without being started, and their Start/Edit actions are drawn over the pane.
Buttons use the icon bake ([mod-icons](mod-icons.md)) and draw before
`ColonistBarStrip.Draw`, so the strip reserves `TerminalWindow.CornerW` at both ends.
`OpenMenu` is handled before key forwarding in every mode, allowing a menu from a
full-screen TUI. The second line is `ScreenView.title` from OSC 0/2, only when non-empty.
When the focused agent exits (including Ctrl+C/Ctrl+D), its pane stays on the stopped agent
without changing the selected session. Temporary sessions that disappear still close their
pane; another session can be opened explicitly.

The colonist strip is part of the title bar, so `HeaderH` is `ColonistBarStrip.BarH`.
`OpenOverPane` puts a bar-opened window on the same Super layer as the pane. In sidebar
layout the window draws no header: `TopBar` owns the name, state and buttons.

## Keys

`MapKey` uses tmux names (`C-Left`, `M-Up`, `S-Right`). Shift is sent only on the alt
screen: editors use shifted arrows and page keys, while zsh/bash treat those sequences as
undefined ([zsh-terminal](ops-zsh-terminal.md)). On the primary screen, Shift+PgUp/PgDn move
the mod's own scrollback by one viewport. Mouse-wheel scrollback uses `SmoothScroll`'s
fractional local position. `TerminalHistory` indexes the overlapping rows in daemon viewport
snapshots by their offset and assembles a local view with one overscan row; requests prefetch
on overlapping half-viewport boundaries in the gesture direction, so skipped integer offsets
remain local without issuing a new capture for every fractional movement. The first shallow
window is warmed while the active pane is still at the live bottom, followed by overlapping
captures up to roughly eight viewports deep. Scrolling extends this lookahead in the gesture
direction after fetching missing visible rows first. Cached history is retained when switching
between live and scrollback, so the first small gesture does not wait for capture. The live
frame's history count bounds local scrolling, including a zero range for empty history.
Legacy frames without that count use the 10,000-line limit until a capture finds the real top.
History growth advances the local offset from the last panel observation, including streamed
frames skipped between draws. Before the history limit, the daemon's extent outranks visual
row-overlap guesses. Old live rows that move into history are fetched again: a TUI may have
rewritten its prompt before scrolling, so neither a previous live frame nor a delayed capture's
live tail can establish those historical rows. Confirmed older history remains cached.
When history exists, a
three-unit overlay bar at the pane's right edge shows the current offset against the daemon-
reported history extent without changing the negotiated terminal width; it can be dragged
directly. A monochrome lock marks the frozen view while scrolled back.

These terminal-specific modifier behaviors are hardcoded: Alt+Z/Alt+X walk the terminal tab
list, including host and ephemeral tabs,
Shift+Enter sends `\e[13;2u` so an agent inserts a newline instead of submitting, Ctrl+C
copies when text is selected and otherwise falls through as SIGINT, Ctrl+V pastes, double-click
publishes its word and triple-click publishes its line to the host's Wayland/X11 PRIMARY
selection, and middle-click pastes that selection (even when an app reports mouse input).
Non-Codex agent panes use the normal
clipboard read; host panes use a text-only read. Codex panes probe the text-only clipboard first,
because Codex's image-paste handler
otherwise reports a missing image for ordinary text; they forward Ctrl+V only for image (or
other non-text) clipboard data. The built-in Codex sandbox therefore includes the X11 and
Wayland display capabilities it needs.
Shift+F1..F12 forwards the F-key to the agent while a bare F-key is the mod's.
Non-letter control chords include Ctrl+Space/@, Ctrl+[, Ctrl+backslash, Ctrl+],
Ctrl+^, and Ctrl+_. Historical clicks stay local; forwarded mouse gestures retain
their release even when Shift changes. In alternate-screen apps without mouse reporting,
horizontal wheel gestures send Left/Right; the dominant axis suppresses touchpad drift.
Mouse-reporting apps still receive only vertical wheel reports.
Selection copy combines displayed text with cached offscreen history rows; if the
range contains a missing row, it leaves the clipboard unchanged.

Unity can lose the semicolon IMGUI event. The terminal therefore checks both the
character stream and named key, deduplicated per frame; Shift suppresses the `;` fallback
when the physical key produced `:`. A consumed event is also checked by `rawType`.
Semicolon uses byte-preserving paste after flushing ordinary text, rather than tmux's
named-key path.

Theme, font, selection, URL, and file-link behavior are covered in
[terminal rendering and links](mod-terminal-rendering.md).
