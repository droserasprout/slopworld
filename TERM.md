# Terminal rewrite: real emulator via alacritty_terminal + tmux control mode

Plan for replacing the `capture-pane` screen-scraper with a real server-side
terminal emulator, to reach VSCode/Gnome-Terminal fidelity (mouse in TUIs,
alt-screen scroll, wide chars, cursor styles, bracketed paste, event-driven).

**Status: shipped.** All four phases landed (commits `d6484fc`, `5b1d7a6`,
`7dad4f7`, `fc0012c`). The daemon now owns a `Term` per session in
`slopd/src/emu.rs`, fed by a control-mode reader in `slopd/src/session.rs`. The
sections below are kept as the design record and the alacritty/tmux API
reference. Left out on purpose: OSC8/URL hyperlinks and cursor colour (optional),
and the SIGWINCH reseed nudge (unneeded in testing so far).

## Decisions (locked)

- **Emulator**: `alacritty_terminal` 0.26.0 (pulls `vte` 0.15.0 as its parser),
  max fidelity. Already added to `slopd/Cargo.toml`.
- **Byte source**: tmux **control mode** (`tmux -C attach`). Keeps everything
  the README sells: session persistence across daemon restarts and
  `tmux -L slopworld attach` from a real terminal.
- **Model**: slopd owns one `Term` per session; the mod becomes a faithful cell
  renderer + mouse-aware input forwarder.

```
tmux -C attach -t <name>   (one control client per running session)
   │  %output %<pane> <octal-escaped bytes>
   ▼
vte::ansi::Processor.advance(&mut Term, &bytes)   (the real VT engine)
   │  grid, cursor(shape/visible), scrollback, TermMode(mouse/alt-screen/paste)
   ▼
slopd render(): rows of SGR-coloured runs (+ cursor + mode flags)
   │  WS, event-driven, coalesced ~60fps
   ▼
mod: draw runs; draw cursor by style; forward input per app mouse mode
```

Input keeps flowing over the existing `tmux send-keys` CLI path (the control
client is a **read-only output pump**), so mouse/raw bytes go via `send-keys -H`
/ `-l`. This decouples input from the control protocol.

## Already fixed (shipped, independent of the rewrite)

- **Cursor didn't move on trailing whitespace** — `capture-pane` trims trailing
  spaces, and change-detection hashed only line text. Fixed in
  `session.rs::poll_session` by folding `(cx,cy)` into `changed`. (Becomes moot
  once the emulator lands, but correct meanwhile.)
- Prior work on this branch: mouse-wheel scrollback (capture-offset), drag
  selection + copy, and the input-lag "nudge". The scroll + nudge get replaced
  by the emulator; selection/copy logic largely survives.

## Why the scraper can't go native

`capture-pane` returns a *pre-rendered* screen (SGR colour only) and never the
raw VT stream, so we can't: forward mouse to apps (scroll `less`, click TUIs,
Claude Code mouse), scroll alt-screen apps, track cursor shape/visibility,
handle wide/CJK width, or bracketed paste. All of these need the real byte
stream + a grid model, which is exactly what this rewrite adds.

## alacritty_terminal 0.26 / vte 0.15 API cheat-sheet

Verified against the vendored crate source. Use these to avoid re-deriving.

- Construct:
  - `alacritty_terminal::term::Config` (Default; `scrolling_history: usize`
    default 10000, `default_cursor_style: CursorStyle`).
  - `Term::<VoidListener>::new(config, &dims, VoidListener)` where
    `VoidListener` is `alacritty_terminal::event::VoidListener` (no-op
    `EventListener`; the trait is just `fn send_event(&self, Event) {}`).
  - `dims: &D where D: alacritty_terminal::grid::Dimensions`. Implement our own:
    `total_lines`/`screen_lines` → rows, `columns` → cols. (History comes from
    `Config.scrolling_history`, not from `total_lines`.) A ready-made
    `term::test::TermSize::new(cols, lines)` exists but may be test/feature
    gated — safer to define our own `Dims`.
- Parser: `vte::ansi::Processor::new()` then
  `processor.advance(&mut term, &bytes)` (Term implements `vte::ansi::Handler`).
  `Processor<T: Timeout = StdSyncHandler>` — the default is fine.
- Read the screen: `term.renderable_content() -> RenderableContent` with:
  - `display_iter: GridIterator<Item = Indexed<&Cell>>` where
    `Indexed { point: Point{ line: Line(i32), column: Column(usize) }, cell }`.
  - `cursor: RenderableCursor { shape: CursorShape, point: Point }`.
  - `display_offset: usize` (0 = live bottom; >0 = scrolled into history).
  - **Row mapping**: `display_iter` yields *absolute* grid lines; at offset `k`
    the topmost visible line is `-k`. So `display_row = point.line.0 + display_offset`,
    giving `0..rows-1`. (From `Grid::display_iter`: start `Line(-offset-1)`,
    end `min(start+screen_lines, bottommost)`.)
- `Cell { c: char, fg: Color, bg: Color, flags: Flags }`.
  - `Color` = `vte::ansi::Color::{ Named(NamedColor), Spec(Rgb{r,g,b}), Indexed(u8) }`.
  - `NamedColor as usize`: Black=0..White=7, BrightBlack=8..BrightWhite=15,
    Foreground=256, Background=257, Cursor=258, Dim*/Bright* higher.
  - `Flags` bitflags: `INVERSE, BOLD, ITALIC, UNDERLINE, WRAPLINE, WIDE_CHAR,
    WIDE_CHAR_SPACER, DIM, HIDDEN, STRIKEOUT, ...`.
- Modes: `term.mode() -> &TermMode` bitflags incl. `SHOW_CURSOR, MOUSE_REPORT_CLICK,
  BRACKETED_PASTE, SGR_MOUSE, MOUSE_MOTION, ALT_SCREEN, MOUSE_DRAG, UTF8_MOUSE,
  ALTERNATE_SCROLL`, plus convenience `MOUSE_MODE`.
- Scroll: `term.scroll_display(alacritty_terminal::grid::Scroll::{Delta(i32),
  PageUp, PageDown, Top, Bottom})`.
- Resize: `term.resize(dims)` (S: Dimensions).
- Cursor style: `term.cursor_style() -> CursorStyle { shape: CursorShape, blinking }`;
  `CursorShape::{ Block, Underline, Beam, HollowBlock, Hidden }`.

## tmux control mode facts (verified)

- Launch per session: `tmux -L <socket> -C attach -t <name>`. Reads commands on
  stdin, writes notifications to stdout. Works against a detached session; needs
  no tty. `window-size manual` (already set on spawn) keeps the pane size fixed
  regardless of the attaching control client.
- Notification lines seen: `%begin/%end` (wrap command replies),
  `%session-changed`, `%window-renamed`, `%window-add`, `%layout-change`,
  `%output %<pane-id> <data>`, `%exit`.
- **`%output` payload escaping**: printable bytes literal; others octal-escaped
  as `\NNN` (3 digits), backslash as `\\`. Example seen:
  `%output %0 \015\012\033[?2004l\015`. Unescape: on `\`, if next is `\` emit
  `\`; if digit, read 3 octal digits → byte.
- `%exit` fires when the session/client ends → detect session death by the
  control child exiting.
- One control client follows only the session it attached to → **one client per
  session** (agents are separate tmux sessions).

## Phased implementation

### Phase 1 — parity, event-driven, real emulator underneath (mod UNCHANGED) [shipped]
Serialize the alacritty grid back into the **same SGR-coloured line wire format**
`capture-pane -e` produced, so the mod needs zero changes to reach parity.

- New `slopd/src/emu.rs`:
  - `SessionEmu { term: Term<VoidListener>, parser: Processor, cols, rows }`.
  - `feed(&mut self, &[u8])` → `parser.advance(&mut term, bytes)`.
  - `resize(cols, rows)` → `term.resize(Dims)`.
  - `render(&self) -> ScreenView`: walk `renderable_content()`, build one string
    per row of SGR runs; skip `WIDE_CHAR_SPACER` cells (emit wide char once);
    trim trailing default cells; compute `cx,cy` from `renderable.cursor` (set
    `cy = rows` to hide when `!SHOW_CURSOR`, shape Hidden, or scrolled off).
  - `scroll_snapshot(&mut self, off) -> ScreenView`: save offset,
    `scroll_display(Delta(off - cur))`, render, restore. Keeps the existing
    "separate scroll frame (off>0)" protocol the mod already speaks. (Normal
    buffer only; alt-screen has no history — that's Phase 3.)
  - **Colour → SGR mapping** (preserve the mod's curated palette by emitting the
    *same SGR codes* tmux would, not resolved RGB):
    - fg: Named 0-7→`30+n`, 8-15→`90+(n-8)`, Foreground/other→`39`;
      `Indexed(i)` i<16→basic, else `38;5;i`; `Spec`→`38;2;r;g;b`.
    - bg: same with `49/40-47/100-107/48;5/48;2`.
    - flags: `BOLD`→`1`, `INVERSE`→`7` (mod only renders bold/reverse/colour).
    - Prefix each line with `\033[0m`.
- New per-session control reader (in `emu.rs` or `control.rs`): spawn
  `tmux -C attach`, `BufReader.lines()`, parse `%output` → unescape → `feed`.
  Coalesce output (~8ms) then render + broadcast on change. `%exit`/child-exit →
  mark dead.
- Rewire `session.rs`/`main.rs`:
  - Drop the `capture-pane` poll loop. Each running session owns a control-reader
    task (start on `start()`/on daemon boot for already-running sessions; stop on
    `stop()`).
  - Keep a lightweight in-memory tick only to re-run state rules (working→idle
    after quiet); no tmux spawns.
  - `send_keys`/`resize` stay on the tmux CLI; `resize` also calls
    `emu.resize()`.
  - Reseed on attach: `capture-pane -pe` current screen → write
    `\033[2J\033[H` + lines(CRLF) + cursor-home to the fresh Term. Good enough;
    alt-screen apps self-correct on next redraw. (Optional later: SIGWINCH nudge
    to force a clean full redraw.)
- State classification: run existing regex rules over the rendered grid text
  (we already have it in-memory) instead of `strip_sgr(capture)`.

**Milestone check**: shells + Claude render identical to today, but latency is
event-driven and the emulator now tracks alt-screen/mouse/scrollback for later.

### Phase 2 — fidelity (extend wire + mod renderer) [shipped]
- Wire additions to the Screen event: per-run **start column** (fixes CJK/emoji
  alignment — draw each run at its true column), **cursor shape** + **visible**,
  `app_mouse` and `alt_screen` flags.
- Mod: draw runs by explicit column; draw cursor per shape (block/bar/underline)
  honouring visibility; keep blink.

### Phase 3 — mouse + scroll forwarding (the payoff for `less`/TUIs) [shipped]
- Mod wheel: if `app_mouse` → send mouse report (new WS msg → `send-keys -H` the
  SGR mouse bytes); else alacritty display-scroll (replace capture-offset). Alt-
  screen + mouse-off + `ALTERNATE_SCROLL` → send arrow keys.
- Mod click/drag: if `app_mouse` → forward as mouse reports; else keep our local
  selection. **Shift = force local selection** (like real terminals).
- Build mouse report bytes from `TermMode` (SGR vs UTF8 vs legacy). alacritty
  gives us the exact mode flags.

### Phase 4 — polish [shipped: bracketed paste + cursor-blink parity]
- Cursor shapes/blink parity, bracketed-paste wrapping (wrap pastes in
  `\033[200~..\033[201~` when `BRACKETED_PASTE`), selection niceties, cursor-
  colour, URL/OSC8 (optional).

## Open questions / gotchas

- **Reseed correctness for idle alt-screen apps** on daemon restart: capture-seed
  writes onto the normal screen; an idle alt-screen app won't redraw. If this
  bites, add a size-toggle SIGWINCH nudge on attach to force a full repaint.
- **Scroll model**: Phase 1 keeps stateless scroll snapshots (mod unchanged). A
  cleaner end state is native alacritty scroll *state* (viewport pins while
  output appends), which would let us simplify the mod's separate live/scroll
  buffers — revisit in Phase 3.
- **Palette**: emitting index-based SGR preserves the mod's muted palette. If we
  later want OSC palette changes (apps that recolour), resolve via
  `term.colors()` and emit truecolor instead.
- **"Claude asks about integration / mouse tmux option"**: still need the exact
  prompt text to diagnose; likely Claude Code reacting to `TERM=tmux`/`screen`
  or offering its tmux/mouse integration. Capture the wording next session.
- **Load**: one control child + one in-memory `Term` per *running* session
  (needed so the top-bar state stays live for unsubscribed sessions). Fine at
  this scale (handful of agents).
