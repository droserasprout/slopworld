# The terminal pane

`TerminalWindow` renders a pane and forwards keys. Almost everything typed goes to
the agent - **Escape included, so leaving is Shift+Escape** - and the few keys the
window keeps are taken first: F12 closes, Alt+1..9 / Alt+0 point it at that
portrait, counting through `AgentColony.InBarOrder`. The same numbers are read on
the map by `TerminalHotkeys`. Game components run *ahead* of the window stack in
`UIRootOnGUI`, so the map half stands down while a pane is open rather than
trusting the pane to have eaten the key. `SnapX`/`SnapY` put every box edge on a
screen pixel.

`TerminalWindow` runs at `Margin` 0 so GUI-group and screen coordinates agree (see
[gotchas](gotchas.md)).

## Size

The pane's size is the window's, not a setting: `NegotiateSize` divides the body
rect by the cell size and sends a `resize` (debounced 0.2s), and keeps asking once
a second while the frames coming back disagree - a fire-and-forget message over a
socket that drops on every redeploy has no other way back. `BOOT_COLS`/`BOOT_ROWS`
in `session.rs` is what a pane wears until someone looks at it.

## Title bar (strip layout)

Carries a gear and a cross; anything that *ends* an agent is in the agents list
instead. `GearIcon` is drawn in code because `TexButton` has no gear and a content
path resolving to null draws an invisible button. Those buttons are drawn
**before** `ColonistBarStrip.Draw`, so the strip keeps their corner clear via
`TerminalWindow.CornerW`, subtracted from both ends of `FitScale`'s room since the
row is centred and the map view lays out the same pixels. `OpenMenu` is on the
right button, taken before the forwarder sees it in every mode: the menu has to be
reachable from inside a full-screen TUI, and no agent here asks for button 2. Line
two of the bar is `ScreenView.title` off OSC 0/2, drawn only when there is one.

The colonist strip is *in* the title bar, hence `HeaderH` is
`ColonistBarStrip.BarH`. `OpenOverPane` puts a window opened from the bar on the
Super layer with the pane, since an ordinary dialog would land underneath.

In the **sidebar layout** the window draws no header at all: `TopBar`
([mod-ui-chrome](mod-ui-chrome.md)) carries the name, the state and those two
buttons, and the body starts below it and right of the column.

## Keys

`MapKey` names a key the way tmux does - `C-Left`, `M-Up`, `S-Right` - and
`send-keys` on the far side turns that into the xterm sequence. **Shift is sent
only on the alt screen.** A full-screen editor asked for the whole terminal and
reads `\e[1;2C` as select-right, but zsh and bash leave that sequence undefined
and zsh answers it with a bell and a stray `C` ([zsh-terminal](zsh-terminal.md)),
where a bare `Up`/`Down`/`Left`/`Right` at least still moved the cursor. So the
prompt keeps the plain arrow and the editor gets its selection.

## `TerminalTheme`

`Sgr.DefaultFg`/`DefaultBg` are properties off it rather than constants, so the
window's own fills follow the scheme. Colours are resolved **into** the runs at
parse time, which is why `Rev` exists - it moves on every scheme change, and both
the run cache (`ScreenBuf.RunsRev`) and the pane's RenderTexture (`_cacheRev`) are
keyed on it, or an idle agent keeps the old palette until it next writes, which on
an idle agent is never. `Get` on an unknown name answers the default; the cursor
override is read as `#rrggbb` or ignored. A block cursor is drawn opaque with the
glyph put back over it in `CursorText`.

## Links

Two sources, the same thing by the time they are drawn.

- `emu.rs` carries the app's own **OSC 8** through into the row (`safe_uri` strips
  controls and caps it).
- `Sgr.Autolink` reads each row once more as *characters* to catch URLs an agent
  merely printed - runs are how a row will be drawn and a URL has no reason to
  respect where one ends, so `Split` cuts the runs against the spans. A run the app
  already linked is left alone.
- **Limitation**: row at a time. A link the app wrapped is two links here, the
  daemon not marking the wrap.
- `TrackHover`/`LinkAt` decide highlight, tooltip and click from one lookup.
  Ctrl+click opens through `POST /api/open`; `open.rs` takes http, https and
  mailto and nothing else, tries `xdg-open`, `gio` and `wslview`, and treats a
  child still alive after `HANDOFF` as success. `Application.OpenURL` is the
  fallback rather than the road.
