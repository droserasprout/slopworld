//! One `SessionEmu` per running session, driving an `alacritty_terminal` VT engine off the
//! raw bytes tmux control mode gives us and serialising back into the SGR-colored line
//! format the mod speaks.

use std::hash::{Hash, Hasher};
use std::sync::{Arc, Mutex};

use alacritty_terminal::event::{Event, EventListener};
use alacritty_terminal::grid::{Dimensions, Scroll};
use alacritty_terminal::index::{Column, Line, Point};
use alacritty_terminal::term::cell::{Cell, Flags, Hyperlink};
use alacritty_terminal::term::{
    ClipboardType, Config, LineDamageBounds, Term, TermDamage, TermMode,
};
use alacritty_terminal::vte::ansi::{Color, CursorShape, CursorStyle, Processor};

/// Our own `Dimensions`, so we don't depend on the test-gated `TermSize`.
struct Dims {
    cols: usize,
    rows: usize,
}

impl Dimensions for Dims {
    fn total_lines(&self) -> usize {
        self.rows
    }
    fn screen_lines(&self) -> usize {
        self.rows
    }
    fn columns(&self) -> usize {
        self.cols
    }
}

/// `cy == rows` hides the cursor. Rows carry `\x1b[<n>G` (CHA) ahead of any run whose true
/// column diverges from the pen - which is where a wide char skipped a cell - and the mod
/// redraws those runs at their absolute column.
pub struct Frame {
    pub lines: Vec<Arc<str>>,
    /// Equality accelerator for the complete visible content. It is derived from cached row
    /// hashes, rather than by scanning the serialized rows in the manager.
    pub content_hash: u64,
    /// Number of rows currently available above the primary live viewport.
    pub history: u32,
    pub cx: u16,
    pub cy: u16,
    /// 0 = block, 1 = underline, 2 = beam.
    pub cursor_shape: u8,
    pub cursor_blink: bool,
    pub app_mouse: bool,
    /// Apps that did not ask - Claude Code among them - leave the drag to the
    /// terminal, which is what lets the pane select text without holding Shift.
    pub app_drag: bool,
    /// The app is on the alternate screen (no scrollback of its own).
    pub alt_screen: bool,
    /// What the app called itself (OSC 0/2 or tmux's `ESC k` title form); empty until it says,
    /// and empty again when it resets.
    pub title: String,
    /// The app rang BEL since the last live render. An event rather than a state, so it is
    /// true on exactly one frame and the session table is what holds it after that.
    pub bell: bool,
}

struct RenderCache {
    cols: u16,
    rows: u16,
    cells: Vec<Vec<Slot>>,
    lines: Vec<Arc<str>>,
    row_hashes: Vec<u64>,
    content_hash: u64,
    display_offset: usize,
    alt_screen: Option<bool>,
    valid: bool,
}

impl RenderCache {
    fn new() -> Self {
        Self {
            cols: 0,
            rows: 0,
            cells: Vec::new(),
            lines: Vec::new(),
            row_hashes: Vec::new(),
            content_hash: 0,
            display_offset: 0,
            alt_screen: None,
            valid: false,
        }
    }

    fn invalidate(&mut self) {
        self.valid = false;
    }
}

/// Everything the VT engine hands *back* rather than draws.
#[derive(Default)]
struct Side {
    /// The last OSC 52 store. One slot rather than a queue: a clipboard holds one thing.
    clip: Option<String>,
    title: Option<String>,
    /// BEL since the last render. A flag rather than a count: twice is still "look at me".
    bell: bool,
}

/// Handles side effects from the mirrored screen: title, clipboard store, and BEL. tmux is the
/// pane's terminal and answers terminal queries itself, so `PtyWrite` must not be sent back.
/// OSC 52 remains copy-only so apps cannot read the operator's clipboard.
#[derive(Clone)]
struct SideSink {
    side: Arc<Mutex<Side>>,
}

// zsh's terminal-title hook uses tmux's private `ESC k title ESC \` sequence rather than
// OSC 0/2. `vte` deliberately leaves that sequence unhandled, which would otherwise print the
// title into the screen immediately before the command's output.
enum TmuxTitleState {
    Ground,
    Escape,
    Body(Vec<u8>),
    BodyEscape(Vec<u8>),
}

impl EventListener for SideSink {
    fn send_event(&self, event: Event) {
        let Ok(mut s) = self.side.lock() else { return };
        match event {
            Event::PtyWrite(_) => {}
            Event::ClipboardStore(ClipboardType::Clipboard, text) => s.clip = Some(text),
            Event::Title(t) => s.title = Some(t),
            Event::ResetTitle => s.title = None,
            Event::Bell => s.bell = true,
            _ => {}
        }
    }
}

pub struct SessionEmu {
    term: Term<SideSink>,
    parser: Processor,
    tmux_title: TmuxTitleState,
    cols: u16,
    rows: u16,
    side: Arc<Mutex<Side>>,
    render_cache: RenderCache,
    // The first game resize reconciles the daemon's boot shape with the pane's real shape. If
    // the tmux capture had no history, a smaller boot viewport would otherwise turn one blank
    // row from that handoff into scrollback before the session has produced any output.
    initial_capture_history: Option<usize>,
}

const TMUX_TITLE_MAX: usize = 512;

impl SessionEmu {
    pub fn new(cols: u16, rows: u16) -> Self {
        let dims = Dims {
            cols: cols as usize,
            rows: rows as usize,
        };
        // A blinking block, so unstyled shells keep the familiar blink; DECSCUSR still
        // overrides it.
        let config = Config {
            default_cursor_style: CursorStyle {
                shape: CursorShape::Block,
                blinking: true,
            },
            ..Config::default()
        };
        let side = Arc::new(Mutex::new(Side::default()));
        let term = Term::new(config, &dims, SideSink { side: side.clone() });
        Self {
            term,
            parser: Processor::new(),
            tmux_title: TmuxTitleState::Ground,
            cols,
            rows,
            side,
            render_cache: RenderCache::new(),
            initial_capture_history: None,
        }
    }

    /// Mark the tmux snapshot as complete so its first UI-driven resize can distinguish real
    /// captured history from rows introduced solely by reconciling the boot dimensions.
    pub fn complete_initial_capture(&mut self) {
        self.initial_capture_history = Some(self.history_lines());
    }

    pub fn feed(&mut self, bytes: &[u8]) {
        let mut plain = Vec::with_capacity(bytes.len());
        for &byte in bytes {
            let state = std::mem::replace(&mut self.tmux_title, TmuxTitleState::Ground);
            self.tmux_title = match state {
                TmuxTitleState::Ground if byte == 0x1b => TmuxTitleState::Escape,
                TmuxTitleState::Ground => {
                    plain.push(byte);
                    TmuxTitleState::Ground
                }
                TmuxTitleState::Escape if byte == b'k' => {
                    self.feed_plain(&plain);
                    plain.clear();
                    TmuxTitleState::Body(Vec::new())
                }
                TmuxTitleState::Escape if byte == 0x1b => {
                    plain.push(0x1b);
                    TmuxTitleState::Escape
                }
                TmuxTitleState::Escape => {
                    plain.extend_from_slice(&[0x1b, byte]);
                    TmuxTitleState::Ground
                }
                TmuxTitleState::Body(title) if byte == 0x1b => TmuxTitleState::BodyEscape(title),
                TmuxTitleState::Body(_) if matches!(byte, 0x18 | 0x1a) => TmuxTitleState::Ground,
                TmuxTitleState::Body(mut title) => {
                    if title.len() < TMUX_TITLE_MAX {
                        title.push(byte);
                        TmuxTitleState::Body(title)
                    } else {
                        recover_tmux_title(&mut plain, title, &[byte]);
                        TmuxTitleState::Ground
                    }
                }
                TmuxTitleState::BodyEscape(title) if byte == b'\\' => {
                    self.finish_tmux_title(title);
                    TmuxTitleState::Ground
                }
                TmuxTitleState::BodyEscape(_) if matches!(byte, 0x18 | 0x1a) => {
                    TmuxTitleState::Ground
                }
                TmuxTitleState::BodyEscape(mut title) if byte == 0x1b => {
                    if title.len() < TMUX_TITLE_MAX {
                        title.push(0x1b);
                        TmuxTitleState::BodyEscape(title)
                    } else {
                        recover_tmux_title(&mut plain, title, &[0x1b, 0x1b]);
                        TmuxTitleState::Ground
                    }
                }
                TmuxTitleState::BodyEscape(mut title) => {
                    if title.len().saturating_add(2) <= TMUX_TITLE_MAX {
                        title.extend_from_slice(&[0x1b, byte]);
                        TmuxTitleState::Body(title)
                    } else {
                        recover_tmux_title(&mut plain, title, &[0x1b, byte]);
                        TmuxTitleState::Ground
                    }
                }
            };
        }
        self.feed_plain(&plain);
    }

    fn feed_plain(&mut self, bytes: &[u8]) {
        if !bytes.is_empty() {
            self.parser.advance(&mut self.term, bytes);
        }
    }

    fn finish_tmux_title(&mut self, bytes: Vec<u8>) {
        let title: String = String::from_utf8_lossy(&bytes)
            .chars()
            .filter(|c| !c.is_control())
            .take(TMUX_TITLE_MAX)
            .collect();
        if let Ok(mut side) = self.side.lock() {
            side.title = if title.is_empty() { None } else { Some(title) };
        }
    }

    /// Left in place if the caller does not ask, so a write in flight never loses the copy
    /// that came after it.
    pub fn take_clip(&mut self) -> Option<String> {
        self.side.lock().ok().and_then(|mut s| s.clip.take())
    }

    /// One-shot, and the frame is where it goes: what holds a bell until someone has looked
    /// at the pane is the session table, this being an event with no state behind it.
    fn take_bell(&self) -> bool {
        self.side
            .lock()
            .map(|mut s| std::mem::take(&mut s.bell))
            .unwrap_or(false)
    }

    fn title(&self) -> String {
        self.side
            .lock()
            .ok()
            .and_then(|s| s.title.clone())
            .unwrap_or_default()
    }

    fn history_lines(&self) -> usize {
        self.term
            .total_lines()
            .saturating_sub(self.term.screen_lines())
    }

    pub fn resize(&mut self, cols: u16, rows: u16) {
        if cols == self.cols && rows == self.rows {
            return;
        }
        let history_before = self.history_lines();
        let initial_capture_history = self.initial_capture_history.take();
        self.cols = cols;
        self.rows = rows;
        self.render_cache.invalidate();
        self.term.resize(Dims {
            cols: cols as usize,
            rows: rows as usize,
        });
        if initial_capture_history == Some(0) && history_before == 0 {
            self.term.grid_mut().clear_history();
        }
    }

    pub fn render(&mut self) -> Frame {
        let mut frame = self.render_frame(false);
        // Taken here rather than in render_frame: a wheel asks for a scroll snapshot off the
        // same emulator, and a bell swallowed by somebody's scrollback never reaches the
        // column.
        frame.bell = self.take_bell();
        frame
    }

    /// `None` when the app isn't asking for mouse reports, or for a drag it didn't opt
    /// into - the caller then falls back to local scroll/selection.
    pub fn mouse_report(&self, m: &MouseInput) -> Option<Vec<u8>> {
        let mode = self.term.mode();
        if !mode.intersects(TermMode::MOUSE_MODE) {
            return None;
        }
        if matches!(m.action, MouseAction::Drag)
            && !mode.intersects(TermMode::MOUSE_MOTION | TermMode::MOUSE_DRAG)
        {
            return None;
        }

        let sgr = mode.contains(TermMode::SGR_MOUSE);
        let utf8 = mode.contains(TermMode::UTF8_MOUSE);
        let release = matches!(m.action, MouseAction::Release);
        let button = m.button as u32 & 3;
        // Low 2 bits pick the button; bit 5 (32) marks motion; wheel is 64+.
        let cb: u32 = match m.action {
            MouseAction::WheelUp => 64,
            MouseAction::WheelDown => 65,
            MouseAction::Drag => button + 32,
            MouseAction::Press => button,
            // Legacy can't say which button released, so it reports 3.
            MouseAction::Release => {
                if sgr {
                    button
                } else {
                    3
                }
            }
        };

        let x = m.col as u32 + 1;
        let y = m.row as u32 + 1;
        let mut out = Vec::new();
        if sgr {
            out.extend_from_slice(b"\x1b[<");
            out.extend_from_slice(format!("{cb};{x};{y}").as_bytes());
            out.push(if release { b'm' } else { b'M' });
        } else {
            out.extend_from_slice(b"\x1b[M");
            push_coord(&mut out, cb + 32, utf8);
            push_coord(&mut out, x + 32, utf8);
            push_coord(&mut out, y + 32, utf8);
        }
        Some(out)
    }

    /// Restores the live view afterwards; the cursor is always hidden in a history
    /// view. Returns the offset actually reached, clamped to history.
    pub fn scroll_snapshot(&mut self, off: u32) -> (Vec<Vec<Slot>>, u32, u32, String) {
        // u32 off the wire, but the alacritty grid counts in isize; a history beyond
        // i32::MAX lines is not a thing a terminal holds.
        let off = off.min(i32::MAX as u32) as i32;
        let cur = self.term.grid().display_offset() as i32;
        self.term.scroll_display(Scroll::Delta(off - cur));
        let achieved = self.term.grid().display_offset() as u32;
        let history = self
            .term
            .total_lines()
            .saturating_sub(self.term.screen_lines())
            .min(u32::MAX as usize) as u32;
        let grid = self.capture_visible_grid();
        let title = self.title();
        self.term.scroll_display(Scroll::Bottom);
        self.render_cache.invalidate();
        (grid, achieved, history, title)
    }

    /// Builds a scrollback frame from a grid captured by `scroll_snapshot`. Runs outside
    /// the emulator lock. The cursor is parked off-screen; a history view has no cursor.
    pub fn frame_from_grid(grid: Vec<Vec<Slot>>, _cols: u16, rows: u16, title: String) -> Frame {
        let lines: Vec<Arc<str>> = grid.iter().map(|r| Arc::from(serialize_row(r))).collect();
        let row_hashes = lines.iter().map(hash_row).collect::<Vec<_>>();
        Frame {
            content_hash: hash_rows(&row_hashes),
            lines,
            history: 0,
            cx: 0,
            cy: rows,
            cursor_shape: 0,
            cursor_blink: false,
            app_mouse: false,
            app_drag: false,
            alt_screen: false,
            title,
            bell: false,
        }
    }

    fn capture_visible_grid(&self) -> Vec<Vec<Slot>> {
        let cols = self.cols as usize;
        let rows = self.rows as usize;
        let content = self.term.renderable_content();
        let offset = content.display_offset as i32;

        let mut grid: Vec<Vec<Slot>> = (0..rows)
            .map(|_| (0..cols).map(|_| Slot::Blank).collect())
            .collect();

        for ind in content.display_iter {
            let row = ind.point.line.0 + offset;
            let col = ind.point.column.0;
            if row < 0 || col >= cols {
                continue;
            }
            let row = row as usize;
            if row >= rows {
                continue;
            }
            let cell = ind.cell;
            grid[row][col] = slot_from_cell(cell);
        }
        grid
    }

    fn render_frame(&mut self, hide_cursor: bool) -> Frame {
        let damage = match self.term.damage() {
            TermDamage::Full => Damage::Full,
            TermDamage::Partial(bounds) => Damage::Partial(bounds.collect()),
        };
        let rows = self.rows as usize;
        let (offset, mode, cursor, alt_screen) = {
            let content = self.term.renderable_content();
            let display_offset = content.display_offset;
            let offset = display_offset as i32;
            let mode = content.mode;
            let cursor = content.cursor;
            let alt_screen = mode.contains(TermMode::ALT_SCREEN);
            let full = matches!(&damage, Damage::Full)
                || !self.render_cache.valid
                || self.render_cache.cols != self.cols
                || self.render_cache.rows != self.rows
                || self.render_cache.display_offset != display_offset
                || self.render_cache.alt_screen != Some(alt_screen)
                || display_offset != 0;

            if full {
                let grid = self.capture_visible_grid();
                let mut lines = Vec::with_capacity(rows);
                let mut row_hashes = Vec::with_capacity(rows);
                for row in &grid {
                    let line: Arc<str> = Arc::from(serialize_row(row));
                    row_hashes.push(hash_row(&line));
                    lines.push(line);
                }
                self.render_cache = RenderCache {
                    cols: self.cols,
                    rows: self.rows,
                    cells: grid,
                    lines,
                    content_hash: hash_rows(&row_hashes),
                    row_hashes,
                    display_offset,
                    alt_screen: Some(alt_screen),
                    valid: true,
                };
            } else if let Damage::Partial(bounds) = damage {
                let cols = self.cols as usize;
                let mut changed = vec![false; rows];
                for bound in bounds {
                    let Some(row) = bound.line.checked_sub(display_offset) else {
                        self.render_cache.invalidate();
                        break;
                    };
                    if row >= rows || bound.left >= cols || bound.right >= cols {
                        self.render_cache.invalidate();
                        break;
                    }
                    // A write over one half of a wide character updates the other half too,
                    // while terminal damage may report only the cell under the cursor. Inspect
                    // both neighbors so the cached leading glyph and spacer cannot diverge from
                    // the terminal grid.
                    let left = bound.left.saturating_sub(1);
                    let right = bound.right.saturating_add(1).min(cols - 1);
                    for col in left..=right {
                        let point =
                            Point::new(Line(row as i32 - display_offset as i32), Column(col));
                        let current = slot_from_cell(&self.term.grid()[point]);
                        if self.render_cache.cells[row][col] != current {
                            self.render_cache.cells[row][col] = current;
                            changed[row] = true;
                        }
                    }
                }
                if !self.render_cache.valid {
                    let grid = self.capture_visible_grid();
                    let mut lines = Vec::with_capacity(rows);
                    let mut row_hashes = Vec::with_capacity(rows);
                    for row in &grid {
                        let line: Arc<str> = Arc::from(serialize_row(row));
                        row_hashes.push(hash_row(&line));
                        lines.push(line);
                    }
                    self.render_cache = RenderCache {
                        cols: self.cols,
                        rows: self.rows,
                        cells: grid,
                        content_hash: hash_rows(&row_hashes),
                        lines,
                        row_hashes,
                        display_offset,
                        alt_screen: Some(alt_screen),
                        valid: true,
                    };
                } else {
                    for (row, changed) in changed.into_iter().enumerate() {
                        if changed {
                            let line: Arc<str> =
                                Arc::from(serialize_row(&self.render_cache.cells[row]));
                            self.render_cache.row_hashes[row] = hash_row(&line);
                            self.render_cache.lines[row] = line;
                        }
                    }
                    self.render_cache.content_hash = hash_rows(&self.render_cache.row_hashes);
                }
            }
            (offset, mode, cursor, alt_screen)
        };
        self.term.reset_damage();

        let crow = cursor.point.line.0 + offset;
        let hidden = hide_cursor
            || cursor.shape == CursorShape::Hidden
            || !mode.contains(TermMode::SHOW_CURSOR)
            || crow < 0
            || crow as usize >= rows;
        let (cx, cy) = if hidden {
            (0u16, self.rows)
        } else {
            (cursor.point.column.0 as u16, crow as u16)
        };

        let cursor_shape = match cursor.shape {
            CursorShape::Underline => 1,
            CursorShape::Beam => 2,
            _ => 0,
        };

        Frame {
            content_hash: self.render_cache.content_hash,
            lines: self.render_cache.lines.clone(),
            history: self
                .term
                .total_lines()
                .saturating_sub(self.term.screen_lines())
                .min(u32::MAX as usize) as u32,
            cx,
            cy,
            cursor_shape,
            cursor_blink: self.term.cursor_style().blinking,
            app_mouse: mode.intersects(TermMode::MOUSE_MODE),
            app_drag: mode.intersects(TermMode::MOUSE_MOTION | TermMode::MOUSE_DRAG),
            alt_screen,
            title: self.title(),
            bell: false,
        }
    }
}

enum Damage {
    Full,
    Partial(Vec<LineDamageBounds>),
}

fn slot_from_cell(cell: &Cell) -> Slot {
    if cell.flags.contains(Flags::WIDE_CHAR_SPACER) {
        Slot::Spacer
    } else {
        Slot::Ch(cell.c, cell.fg, cell.bg, cell.flags, cell.hyperlink())
    }
}

fn hash_row(row: &Arc<str>) -> u64 {
    let mut h = std::collections::hash_map::DefaultHasher::new();
    row.hash(&mut h);
    h.finish()
}

fn hash_rows(rows: &[u64]) -> u64 {
    let mut h = std::collections::hash_map::DefaultHasher::new();
    rows.hash(&mut h);
    h.finish()
}

fn recover_tmux_title(plain: &mut Vec<u8>, title: Vec<u8>, suffix: &[u8]) {
    // A malformed private title must not turn into a permanent output sink. Give the bytes
    // back to the ordinary VT parser, matching the behavior before ESC-k support was added.
    plain.extend_from_slice(b"\x1bk");
    plain.extend_from_slice(&title);
    plain.extend_from_slice(suffix);
}

#[derive(Clone, PartialEq, Eq)]
pub(crate) enum Slot {
    /// Never-touched cell: a default-attribute space.
    Blank,
    /// The second half of a wide char; produces no output of its own.
    Spacer,
    Ch(char, Color, Color, Flags, Option<Hyperlink>),
}

/// Render-ready cell style; `Color::Named` values can emit no code, so compare normalized pens
/// rather than raw attributes and avoid formatting strings per cell.
#[derive(Clone, Copy, PartialEq, Eq, Default)]
struct Pen {
    bold: bool,
    dim: bool,
    inverse: bool,
    fg: Ink,
    bg: Ink,
}

/// A color as the offset it contributes to `30`/`40`, rather than as the string it becomes.
#[derive(Clone, Copy, PartialEq, Eq, Default)]
enum Ink {
    /// Emits nothing: the mod's own default for that half.
    #[default]
    Unsaid,
    Basic(u16),
    Indexed(u8),
    Rgb(u8, u8, u8),
}

/// Index-based rather than resolved RGB, so the mod's palette keeps its look.
fn ink(c: Color) -> Ink {
    match c {
        Color::Named(n) => match n as u16 {
            i if i <= 7 => Ink::Basic(i),
            i if i <= 15 => Ink::Basic(60 + (i - 8)),
            _ => Ink::Unsaid,
        },
        Color::Indexed(i) => match i {
            0..=7 => Ink::Basic(i as u16),
            8..=15 => Ink::Basic(60 + (i as u16 - 8)),
            _ => Ink::Indexed(i),
        },
        Color::Spec(rgb) => Ink::Rgb(rgb.r, rgb.g, rgb.b),
    }
}

impl Pen {
    fn of(fg: Color, bg: Color, flags: Flags) -> Self {
        Self {
            bold: flags.contains(Flags::BOLD),
            // Dropping this is what drew Claude Code's greyed-out completions as ordinary
            // text: the color on a faint cell is the *full* one, the dimming being an
            // attribute rather than a palette entry.
            dim: flags.contains(Flags::DIM),
            inverse: flags.contains(Flags::INVERSE),
            fg: ink(fg),
            bg: ink(bg),
        }
    }

    /// Straight onto the row's buffer. The mod renders only bold, faint, reverse and color,
    /// so that is all this emits, and the order is the one the runs were always written in.
    fn write(&self, out: &mut String) {
        if *self == Self::default() {
            out.push_str("\x1b[0m");
            return;
        }
        out.push_str("\x1b[0");
        if self.bold {
            out.push_str(";1");
        }
        if self.dim {
            out.push_str(";2");
        }
        if self.inverse {
            out.push_str(";7");
        }
        write_ink(out, self.fg, 30);
        write_ink(out, self.bg, 40);
        out.push('m');
    }
}

fn write_ink(out: &mut String, ink: Ink, base: u16) {
    use std::fmt::Write;
    match ink {
        Ink::Unsaid => {}
        Ink::Basic(off) => {
            let _ = write!(out, ";{}", base + off);
        }
        Ink::Indexed(i) => {
            let _ = write!(out, ";{};5;{i}", base + 8);
        }
        Ink::Rgb(r, g, b) => {
            let _ = write!(out, ";{};2;{r};{g};{b}", base + 8);
        }
    }
}

/// Serialize self-contained rows with reset/trimmed defaults and only necessary CHA, pen, and
/// link updates; avoid per-cell allocation.
fn serialize_row(row: &[Slot]) -> String {
    let mut out = String::from("\x1b[0m");

    let last = row.iter().rposition(|s| match s {
        Slot::Ch(c, fg, bg, flags, link) => {
            !(*c == ' '
                && ink(*fg) == Ink::Unsaid
                && ink(*bg) == Ink::Unsaid
                && flags.is_empty()
                && link.is_none())
        }
        Slot::Spacer | Slot::Blank => false,
    });
    let Some(last) = last else { return out };

    let mut cur_pen = Pen::default();
    // Compared raw and sanitised only on a change: `safe_uri` allocates, and a link covers a
    // run of cells rather than one.
    let mut cur_uri: &str = "";
    // Where the mod's pen lands with no CHA: one column per emitted char.
    let mut expected = 0usize;
    for (col, slot) in row[..=last].iter().enumerate() {
        let (c, pen, uri) = match slot {
            Slot::Spacer => continue,
            Slot::Blank => (' ', Pen::default(), ""),
            Slot::Ch(c, fg, bg, flags, link) => (
                *c,
                Pen::of(*fg, *bg, *flags),
                link.as_ref().map(|h| h.uri()).unwrap_or_default(),
            ),
        };
        if col != expected {
            use std::fmt::Write;
            let _ = write!(out, "\x1b[{}G", col + 1);
            expected = col;
        }
        if pen != cur_pen {
            pen.write(&mut out);
            cur_pen = pen;
        }
        if uri != cur_uri {
            use std::fmt::Write;
            let _ = write!(out, "\x1b]8;;{}\x1b\\", safe_uri(uri));
            cur_uri = uri;
        }
        out.push(c);
        expected += 1;
    }
    if !cur_uri.is_empty() {
        out.push_str("\x1b]8;;\x1b\\");
    }
    out
}

fn safe_uri(uri: &str) -> String {
    uri.chars().filter(|c| !c.is_control()).take(2048).collect()
}

/// `button` is 0/1/2 = left/mid/right for press/release/drag, ignored for the
/// wheel.
pub enum MouseAction {
    Press,
    Release,
    Drag,
    WheelUp,
    WheelDown,
}

impl MouseAction {
    pub fn parse(s: &str) -> Option<Self> {
        match s {
            "press" => Some(Self::Press),
            "release" => Some(Self::Release),
            "drag" => Some(Self::Drag),
            "wheelup" => Some(Self::WheelUp),
            "wheeldown" => Some(Self::WheelDown),
            _ => None,
        }
    }
}

pub struct MouseInput {
    pub action: MouseAction,
    pub button: u8,
    pub col: u16,
    pub row: u16,
}

/// Legacy/UTF-8 coordinate byte: raw, or a UTF-8 char past 127 under 1005.
fn push_coord(out: &mut Vec<u8>, v: u32, utf8: bool) {
    if utf8 && v > 127 {
        if let Some(c) = char::from_u32(v) {
            let mut buf = [0u8; 4];
            out.extend_from_slice(c.encode_utf8(&mut buf).as_bytes());
            return;
        }
    }
    out.push(v.min(255) as u8);
}

/// Line shape: `%output %<pane> <data>`. Raw bytes, never a `&str`: tmux emits UTF-8 literally
/// and splits chunks on arbitrary byte boundaries, so a line can end mid-character. The halves
/// flow through to the VT parser, which reassembles across feeds.
pub fn parse_output(line: &[u8]) -> Option<Vec<u8>> {
    let rest = line.strip_prefix(b"%output ".as_slice())?;
    let sp = rest.iter().position(|&b| b == b' ')?;
    Some(unescape(&rest[sp + 1..]))
}

/// Printable bytes are literal, `\\` is a backslash, everything else is a 3-digit
/// octal escape.
pub fn unescape(b: &[u8]) -> Vec<u8> {
    let mut out = Vec::with_capacity(b.len());
    let mut i = 0;
    while i < b.len() {
        if b[i] == b'\\' && i + 1 < b.len() {
            if b[i + 1] == b'\\' {
                out.push(b'\\');
                i += 2;
                continue;
            }
            if i + 3 < b.len()
                && b[i + 1].is_ascii_digit()
                && b[i + 2].is_ascii_digit()
                && b[i + 3].is_ascii_digit()
            {
                let n = (b[i + 1] - b'0') as u32 * 64
                    + (b[i + 2] - b'0') as u32 * 8
                    + (b[i + 3] - b'0') as u32;
                out.push(n as u8);
                i += 4;
                continue;
            }
        }
        out.push(b[i]);
        i += 1;
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn unescapes_octal_and_backslash() {
        // %output %0 \015\012\033[?2004l\015
        assert_eq!(
            unescape(b"\\015\\012\\033[?2004l\\015"),
            b"\r\n\x1b[?2004l\r"
        );
        assert_eq!(unescape(b"a\\\\b"), b"a\\b");
        assert_eq!(unescape(b"hi"), b"hi");
    }

    #[test]
    fn parses_output_line() {
        assert_eq!(parse_output(b"%output %0 \\015hi").unwrap(), b"\rhi");
        assert!(parse_output(b"%exit").is_none());
        assert!(parse_output(b"%session-changed $0 name").is_none());
        // A line ending mid-UTF-8 must still parse; the raw bytes pass through for the VT
        // parser to reassemble.
        assert_eq!(parse_output(b"%output %0 A\xf0\x9f").unwrap(), b"A\xf0\x9f");
    }

    #[test]
    fn renders_plain_text() {
        let mut e = SessionEmu::new(20, 3);
        e.feed(b"hello");
        let f = e.render();
        assert_eq!(f.lines.len(), 3);
        assert_eq!(f.lines[0].as_ref(), "\x1b[0mhello");
        assert_eq!((f.cx, f.cy), (5, 0));
        assert_eq!(f.cursor_shape, 0);
        assert!(f.cursor_blink);
    }

    #[test]
    fn repeated_renders_reuse_cached_rows() {
        let mut e = SessionEmu::new(20, 3);
        e.feed(b"hello");
        let first = e.render();
        let second = e.render();

        assert_eq!(first.content_hash, second.content_hash);
        assert!(first
            .lines
            .iter()
            .zip(&second.lines)
            .all(|(old, new)| Arc::ptr_eq(old, new)));
    }

    #[test]
    fn cursor_only_damage_does_not_rebuild_rows() {
        let mut e = SessionEmu::new(20, 3);
        e.feed(b"hello");
        let first = e.render();
        e.feed(b"\x1b[2;8H");
        let second = e.render();

        assert!(first
            .lines
            .iter()
            .zip(&second.lines)
            .all(|(old, new)| Arc::ptr_eq(old, new)));
        assert_eq!(first.content_hash, second.content_hash);
        assert_eq!((second.cx, second.cy), (7, 1));
    }

    #[test]
    fn one_cell_damage_rebuilds_only_its_row() {
        let mut e = SessionEmu::new(20, 3);
        e.feed(b"row zero\r\nrow one");
        let first = e.render();
        e.feed(b"\x1b[2;4HX");
        let second = e.render();

        assert!(Arc::ptr_eq(&first.lines[0], &second.lines[0]));
        assert!(!Arc::ptr_eq(&first.lines[1], &second.lines[1]));
        assert_ne!(first.content_hash, second.content_hash);
    }

    #[test]
    fn hyperlink_damage_rebuilds_a_row_when_the_uri_changes() {
        let mut e = SessionEmu::new(20, 2);
        e.feed(b"\x1b]8;;https://a.example\x1b\\linked\x1b]8;;\x1b\\");
        let first = e.render();
        e.feed(b"\x1b[1;1H\x1b]8;;https://b.example\x1b\\linked\x1b]8;;\x1b\\");
        let second = e.render();

        assert!(!Arc::ptr_eq(&first.lines[0], &second.lines[0]));
        assert!(second.lines[0].contains("https://b.example"));
        assert!(!second.lines[0].contains("https://a.example"));
    }

    #[test]
    fn wide_character_damage_updates_the_spacer_cell() {
        let mut actual = SessionEmu::new(20, 2);
        actual.feed("你X".as_bytes());
        let first = actual.render();
        actual.feed(b"\x1b[1;2HY");
        let actual_frame = actual.render();

        // Force a full refresh in the reference emulator so the cached partial path is compared
        // with the complete grid after writing over the wide character's spacer.
        let mut expected = SessionEmu::new(20, 2);
        expected.feed("你X".as_bytes());
        let _ = expected.render();
        let _ = expected.scroll_snapshot(1);
        expected.feed(b"\x1b[1;2HY");
        let expected_frame = expected.render();

        assert!(!Arc::ptr_eq(&first.lines[0], &actual_frame.lines[0]));
        assert_eq!(actual_frame.lines, expected_frame.lines);
    }

    #[test]
    fn accumulated_damage_is_compared_against_the_final_grid() {
        let mut e = SessionEmu::new(20, 2);
        e.feed(b"start");
        let first = e.render();
        e.feed(b"\x1b[1;1HA");
        e.feed(b"\x1b[1;2HB");
        let frame = e.render();

        assert!(!Arc::ptr_eq(&first.lines[0], &frame.lines[0]));
        assert!(frame.lines[0].contains("AB"));
    }

    #[test]
    fn style_only_damage_changes_content_hash() {
        let mut e = SessionEmu::new(20, 2);
        e.feed(b"X");
        let first = e.render();
        e.feed(b"\x1b[1;1H\x1b[31mX");
        let second = e.render();

        assert!(!Arc::ptr_eq(&first.lines[0], &second.lines[0]));
        assert_ne!(first.lines[0], second.lines[0]);
        assert_ne!(first.content_hash, second.content_hash);
    }

    #[test]
    fn erasing_text_rebuilds_the_row_to_blank() {
        let mut e = SessionEmu::new(20, 2);
        e.feed(b"text");
        let first = e.render();
        e.feed(b"\x1b[1;1H\x1b[2K");
        let frame = e.render();

        assert!(!Arc::ptr_eq(&first.lines[0], &frame.lines[0]));
        assert_eq!(frame.lines[0].as_ref(), "\x1b[0m");
    }

    #[test]
    fn resize_and_scroll_force_full_refreshes() {
        let mut e = SessionEmu::new(20, 3);
        e.feed(b"one\r\ntwo\r\nthree\r\nfour");
        let before_resize = e.render();

        e.resize(20, 2);
        let resized = e.render();
        assert_eq!(resized.lines.len(), 2);
        assert!(before_resize
            .lines
            .iter()
            .take(2)
            .zip(&resized.lines)
            .all(|(old, new)| !Arc::ptr_eq(old, new)));

        let _ = e.scroll_snapshot(1);
        let after_scroll = e.render();
        assert!(resized
            .lines
            .iter()
            .zip(&after_scroll.lines)
            .all(|(old, new)| !Arc::ptr_eq(old, new)));
    }

    #[test]
    fn alternate_screen_transitions_force_full_refreshes() {
        let mut e = SessionEmu::new(20, 2);
        e.feed(b"primary");
        let primary_before = e.render();

        e.feed(b"\x1b[?1049halt");
        let alternate = e.render();
        assert!(alternate.alt_screen);
        assert!(primary_before
            .lines
            .iter()
            .zip(&alternate.lines)
            .all(|(old, new)| !Arc::ptr_eq(old, new)));

        e.feed(b"\x1b[?1049l");
        let primary = e.render();
        assert!(!primary.alt_screen);
        assert!(alternate
            .lines
            .iter()
            .zip(&primary.lines)
            .all(|(old, new)| !Arc::ptr_eq(old, new)));
    }

    #[test]
    fn metadata_only_damage_reuses_rows() {
        let mut e = SessionEmu::new(20, 2);
        e.feed(b"stable");
        let first = e.render();
        e.feed(b"\x1b]0;new title\x07\x1b[?1000h");
        let second = e.render();

        assert!(first
            .lines
            .iter()
            .zip(&second.lines)
            .all(|(old, new)| Arc::ptr_eq(old, new)));
        assert_eq!(first.content_hash, second.content_hash);
        assert_eq!(second.title, "new title");
        assert!(second.app_mouse);
    }

    #[test]
    fn maps_color_to_index_sgr() {
        let mut e = SessionEmu::new(20, 2);
        e.feed(b"\x1b[1;31mX\x1b[0m");
        let f = e.render();
        assert_eq!(f.lines[0].as_ref(), "\x1b[0m\x1b[0;1;31mX");
    }

    // Faint is an attribute rather than a color, so dropping it here left the mod nothing to
    // tell a completion hint from the line above it.
    #[test]
    fn carries_faint_through() {
        let mut e = SessionEmu::new(20, 2);
        e.feed(b"\x1b[2mhint\x1b[0m");
        let f = e.render();
        assert_eq!(f.lines[0].as_ref(), "\x1b[0m\x1b[0;2mhint");
    }

    #[test]
    fn trims_trailing_default_cells() {
        let mut e = SessionEmu::new(20, 2);
        e.feed(b"ab");
        let f = e.render();
        assert_eq!(f.lines[0].as_ref(), "\x1b[0mab");
        assert_eq!(f.lines[1].as_ref(), "\x1b[0m");
    }

    #[test]
    fn wide_char_emits_cha_for_following_run() {
        let mut e = SessionEmu::new(20, 2);
        // A CJK char occupies two cells, so the run after it must be re-anchored with CHA
        // to column 3.
        e.feed("\u{4f60}X".as_bytes());
        let f = e.render();
        assert_eq!(f.lines[0].as_ref(), "\x1b[0m\u{4f60}\x1b[3GX");
    }

    #[test]
    fn wraps_a_hyperlinked_run_in_osc8() {
        let mut e = SessionEmu::new(20, 2);
        e.feed(b"go \x1b]8;;https://example.com\x1b\\here\x1b]8;;\x1b\\ now");
        let f = e.render();
        assert_eq!(
            f.lines[0],
            "\x1b[0mgo \x1b]8;;https://example.com\x1b\\here\x1b]8;;\x1b\\ now".into()
        );
    }

    #[test]
    fn closes_a_hyperlink_left_open_at_the_margin() {
        let mut e = SessionEmu::new(8, 2);
        e.feed(b"\x1b]8;;https://a.example\x1b\\linked");
        let f = e.render();
        assert!(f.lines[0].ends_with("linked\x1b]8;;\x1b\\"));
    }

    #[test]
    fn strips_escapes_out_of_a_uri() {
        assert_eq!(safe_uri("https://a\u{1b}b\u{7}c"), "https://abc");
        assert_eq!(safe_uri(&"x".repeat(4000)).len(), 2048);
    }

    #[test]
    fn no_mouse_report_when_app_isnt_listening() {
        let e = SessionEmu::new(20, 5);
        let m = MouseInput {
            action: MouseAction::Press,
            button: 0,
            col: 3,
            row: 2,
        };
        assert!(e.mouse_report(&m).is_none());
    }

    #[test]
    fn sgr_mouse_report_encodes_click() {
        let mut e = SessionEmu::new(20, 5);
        e.feed(b"\x1b[?1000h\x1b[?1006h"); // click reporting + SGR
        let press = e
            .mouse_report(&MouseInput {
                action: MouseAction::Press,
                button: 0,
                col: 3,
                row: 2,
            })
            .unwrap();
        assert_eq!(press, b"\x1b[<0;4;3M");
        let release = e
            .mouse_report(&MouseInput {
                action: MouseAction::Release,
                button: 0,
                col: 3,
                row: 2,
            })
            .unwrap();
        assert_eq!(release, b"\x1b[<0;4;3m");
        let wheel = e
            .mouse_report(&MouseInput {
                action: MouseAction::WheelUp,
                button: 0,
                col: 0,
                row: 0,
            })
            .unwrap();
        assert_eq!(wheel, b"\x1b[<64;1;1M");
    }

    #[test]
    fn legacy_mouse_report_offsets_by_32() {
        let mut e = SessionEmu::new(20, 5);
        e.feed(b"\x1b[?1000h"); // click reporting, no SGR
        let press = e
            .mouse_report(&MouseInput {
                action: MouseAction::Press,
                button: 0,
                col: 0,
                row: 0,
            })
            .unwrap();
        // ESC [ M, then button+32, col+1+32, row+1+32.
        assert_eq!(press, b"\x1b[M\x20\x21\x21");
    }

    #[test]
    fn reports_cursor_shape_and_modes() {
        let mut e = SessionEmu::new(20, 2);
        // DECSCUSR 6 -> steady bar; enable SGR mouse click reporting.
        e.feed(b"\x1b[6 q\x1b[?1006h\x1b[?1000h");
        let f = e.render();
        assert_eq!(f.cursor_shape, 2);
        assert!(f.app_mouse);
        assert!(!f.alt_screen);
        assert!(!f.cursor_blink);
        // Clicks only: the drag is still the terminal's to select with.
        assert!(!f.app_drag);
        e.feed(b"\x1b[?1002h");
        assert!(e.render().app_drag);
    }

    #[test]
    fn takes_the_title_the_app_states() {
        let mut e = SessionEmu::new(20, 2);
        assert_eq!(e.render().title, "");
        e.feed(b"\x1b]0;claude - slopworld\x07");
        assert_eq!(e.render().title, "claude - slopworld");
        // OSC 2 is the same errand, and an empty one is a reset.
        e.feed(b"\x1b]2;another\x1b\\");
        assert_eq!(e.render().title, "another");
        e.feed(b"\x1b]0;\x07");
        assert_eq!(e.render().title, "");
    }

    #[test]
    fn takes_tmux_titles_without_printing_them() {
        let mut e = SessionEmu::new(40, 2);
        e.feed(b"\x1bkec");
        e.feed(b"ho\x1b\\one two three");

        let frame = e.render();
        assert_eq!(frame.title, "echo");
        assert!(frame.lines.iter().all(|line| !line.contains("echo")));
        assert!(frame
            .lines
            .iter()
            .any(|line| line.contains("one two three")));
    }

    #[test]
    fn an_unterminated_tmux_title_eventually_returns_to_terminal_output() {
        let mut e = SessionEmu::new(80, 4);
        let mut bytes = b"\x1bk".to_vec();
        bytes.extend(std::iter::repeat_n(b'x', TMUX_TITLE_MAX));
        bytes.extend_from_slice(b" visible-after-bad-title");
        e.feed(&bytes);

        let frame = e.render();
        assert_eq!(frame.title, "");
        assert!(frame
            .lines
            .iter()
            .any(|line| line.contains("visible-after-bad-title")));
    }

    #[test]
    fn cancel_abandons_an_unterminated_tmux_title() {
        let mut e = SessionEmu::new(40, 2);
        e.feed(b"\x1bkbroken\x18visible");

        let frame = e.render();
        assert_eq!(frame.title, "");
        assert!(frame.lines.iter().any(|line| line.contains("visible")));
        assert!(frame.lines.iter().all(|line| !line.contains("broken")));
    }

    #[test]
    fn takes_a_bell_once_per_frame() {
        let mut e = SessionEmu::new(20, 2);
        assert!(!e.render().bell);
        e.feed(b"ready\x07");
        // The frame that follows the ring carries it, and only that one: what holds a bell
        // until somebody looks is the session table.
        assert!(e.render().bell);
        assert!(!e.render().bell);
        // Twice between renders is still one bell.
        e.feed(b"\x07\x07");
        assert!(e.render().bell);
        // A scroll snapshot must not swallow it.
        e.feed(b"\x07");
        e.scroll_snapshot(0);
        assert!(e.render().bell);
    }

    #[test]
    fn takes_an_osc52_copy() {
        let mut e = SessionEmu::new(20, 2);
        assert!(e.take_clip().is_none());
        // "hello world", base64, for the clipboard selection.
        e.feed(b"\x1b]52;c;aGVsbG8gd29ybGQ=\x07");
        assert_eq!(e.take_clip().as_deref(), Some("hello world"));
        // Draining is one-shot.
        assert!(e.take_clip().is_none());
        // Only the last one survives: a clipboard holds one thing.
        e.feed(b"\x1b]52;c;Zmlyc3Q=\x07\x1b]52;c;c2Vjb25k\x07");
        assert_eq!(e.take_clip().as_deref(), Some("second"));
        // The primary selection is not the clipboard, and we have no tool for it.
        e.feed(b"\x1b]52;p;cHJpbWFyeQ==\x07");
        assert!(e.take_clip().is_none());
    }

    #[test]
    fn terminal_queries_are_only_mirrored() {
        let mut e = SessionEmu::new(20, 2);
        // tmux, not this mirror, owns the pane and answers both queries.
        e.feed(b"\x1b[c\x1b[6n");
        let f = e.render();
        assert_eq!(
            f.lines.iter().map(AsRef::as_ref).collect::<Vec<&str>>(),
            ["\x1b[0m", "\x1b[0m"]
        );
        assert_eq!((f.cx, f.cy), (0, 0));
    }

    #[test]
    fn seeding_a_full_viewport_does_not_create_spurious_history() {
        let mut e = SessionEmu::new(20, 4);
        e.feed(b"\x1b[H\x1b[0mline-0\r\nline-1\r\nline-2\r\nline-3\x1b[4;1H");

        assert_eq!(e.render().history, 0);
    }

    #[test]
    fn resizing_a_seeded_viewport_does_not_create_spurious_history() {
        let rows = 34;
        let mut e = SessionEmu::new(20, rows);
        let mut seed = String::from("\x1b[H\x1b[0m");
        for row in 0..rows {
            if row > 0 {
                seed.push_str("\r\n");
            }
            seed.push_str(&format!("line-{row}"));
        }
        seed.push_str(&format!("\x1b[{rows};1H"));
        e.feed(seed.as_bytes());
        e.complete_initial_capture();

        e.resize(20, rows - 1);
        assert_eq!(e.render().history, 0);
    }

    #[test]
    fn resizing_an_initial_capture_preserves_real_history() {
        let rows = 34;
        let mut e = SessionEmu::new(20, rows);
        let mut seed = String::from("\x1b[H\x1b[0m");
        for row in 0..=rows {
            if row > 0 {
                seed.push_str("\r\n");
            }
            seed.push_str(&format!("line-{row}"));
        }
        e.feed(seed.as_bytes());
        assert_eq!(e.render().history, 1);
        e.complete_initial_capture();

        e.resize(20, rows - 1);
        assert!(e.render().history >= 1);
    }
}
