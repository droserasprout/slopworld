//! Each running session has one `SessionEmu` with an `alacritty_terminal` VT engine.
//! It processes raw bytes from tmux control mode and produces lines with SGR colors for the mod.

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

mod handler;
mod serialize;

use serialize::serialize_row;

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

/// `cy == rows` hides the cursor. Rows carry `\x1b[<n>G` (CHA) immediately after wide
/// glyphs, including at the trimmed tail, to preserve their occupied end for the mod.
#[derive(Clone)]
pub struct Frame {
    pub lines: Vec<Arc<str>>,
    /// Equality accelerator for the complete visible content. It is derived from cached row
    /// hashes, rather than by scanning the serialized rows in the manager.
    pub content_hash: u64,
    /// Content with faint decorative particles normalized to their background.
    pub activity_hash: u64,
    /// Number of rows currently available above the primary live viewport.
    pub history: u32,
    pub cx: u16,
    pub cy: u16,
    /// 0 = block, 1 = underline, 2 = beam.
    pub cursor_shape: u8,
    pub cursor_blink: bool,
    pub app_mouse: bool,
    /// Whether the application requests mouse drag reports.
    /// Otherwise, the terminal uses drags to select text without Shift.
    pub app_drag: bool,
    /// The app is on the alternate screen (no scrollback of its own).
    pub alt_screen: bool,
    /// The application title from OSC 0/2 or the tmux `ESC k` sequence.
    /// Empty before the application sets a title and after a reset.
    pub title: String,
    /// Whether the application sent BEL since the last live render.
    /// True for one frame. The session table preserves the notification after that frame.
    pub bell: bool,
}

struct RenderCache {
    cols: u16,
    rows: u16,
    cells: Vec<Vec<Slot>>,
    lines: Vec<Arc<str>>,
    row_hashes: Vec<u64>,
    activity_row_hashes: Vec<u64>,
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
            activity_row_hashes: Vec::new(),
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

/// VT engine events that do not draw screen content.
#[derive(Default)]
struct Side {
    /// The latest OSC 52 clipboard write. Each write replaces the previous value.
    clip: Option<String>,
    title: Option<String>,
    /// Whether BEL occurred since the last render. Multiple events set the same flag.
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
    history_extent_cache: Option<u32>,
    redraw_clear: bool,
}

const TMUX_TITLE_MAX: usize = 512;

impl SessionEmu {
    pub fn new(cols: u16, rows: u16) -> Self {
        let dims = Dims {
            cols: cols as usize,
            rows: rows as usize,
        };
        // Use a blinking block cursor by default. DECSCUSR can override this style.
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
            history_extent_cache: None,
            redraw_clear: false,
        }
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

    /// A pager can clear the viewport before tmux has delivered its replacement rows.
    /// The reader uses this hint to avoid publishing that intermediate screen.
    pub fn take_redraw_clear(&mut self) -> bool {
        std::mem::take(&mut self.redraw_clear)
    }

    fn feed_plain(&mut self, bytes: &[u8]) {
        if !bytes.is_empty() {
            let mut history_dirty = false;
            self.parser.advance(
                &mut handler::Mirror {
                    term: &mut self.term,
                    cache: &mut self.render_cache,
                    history_dirty: &mut history_dirty,
                    redraw_clear: &mut self.redraw_clear,
                },
                bytes,
            );
            if history_dirty {
                self.history_extent_cache = None;
            }
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

    /// Take the latest clipboard write. Keep it until the caller requests it.
    /// This preserves a new copy while an earlier write is in progress.
    pub fn take_clip(&mut self) -> Option<String> {
        self.side.lock().ok().and_then(|mut s| s.clip.take())
    }

    /// Take the BEL event for the next live frame.
    /// The session table preserves the notification until the user views the pane.
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

    fn history_extent(&mut self) -> u32 {
        if let Some(extent) = self.history_extent_cache {
            return extent;
        }
        // Inline TUIs can scroll untouched top padding into history while inserting
        // their first header (Codex uses a short DECSTBM region plus a leading LF).
        // Keep the VT grid intact, but don't offer that empty prefix as scrollback.
        // Blank separators after actual text and styled cells remain accessible.
        let blank = Cell::default();
        let mut extent = self.history_lines();
        while extent > 0
            && (0..self.cols as usize)
                .all(|col| self.term.grid()[Line(-(extent as i32))][Column(col)] == blank)
        {
            extent -= 1;
        }
        let extent = extent.min(u32::MAX as usize) as u32;
        self.history_extent_cache = Some(extent);
        extent
    }

    pub fn resize(&mut self, cols: u16, rows: u16) {
        if cols == self.cols && rows == self.rows {
            return;
        }
        let history_before = self.history_lines();
        self.cols = cols;
        self.rows = rows;
        self.render_cache.invalidate();
        self.history_extent_cache = None;
        self.term.resize(Dims {
            cols: cols as usize,
            rows: rows as usize,
        });
        // A bottom-positioned prompt can push blank top padding into history on each
        // shrink, not just the first boot-size negotiation. Discard only an entirely
        // blank history created by this resize. Existing history and displaced text stay.
        let history_after = self.history_lines();
        let blank = Cell::default();
        if history_before == 0
            && history_after > 0
            && (1..=history_after).all(|row| {
                (0..cols as usize)
                    .all(|col| self.term.grid()[Line(-(row as i32))][Column(col)] == blank)
            })
        {
            self.term.grid_mut().clear_history();
        }
    }

    pub fn render(&mut self) -> Frame {
        let _perf = crate::perf::timer("emulator-render");
        let mut frame = self.render_frame(false);
        // Take BEL here because render_frame also serves scrollback requests.
        // A scrollback request must not consume the notification before it reaches the column.
        frame.bell = self.take_bell();
        frame
    }

    /// Return `None` if the application does not request mouse reports or the requested drag type.
    /// The caller then uses local scrolling or selection.
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
        // The lowest two bits select the button. Bit 5 (32) indicates motion. Wheel codes start at 64.
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

    /// Capture history with a hidden cursor, then restore the live view.
    /// Return the actual offset, limited to the available history.
    pub fn scroll_snapshot(&mut self, off: u32) -> (Vec<Vec<Slot>>, u32, u32, String) {
        // The protocol offset is u32, but the alacritty grid uses signed offsets.
        // Limit the offset to i32::MAX lines before conversion.
        let history = self.history_extent();
        let off = off.min(history).min(i32::MAX as u32) as i32;
        let cur = self.term.grid().display_offset() as i32;
        self.term.scroll_display(Scroll::Delta(off - cur));
        let achieved = self.term.grid().display_offset() as u32;
        let grid = self.capture_visible_grid();
        let title = self.title();
        self.term.scroll_display(Scroll::Bottom);
        self.render_cache.invalidate();
        (grid, achieved, history, title)
    }

    /// Build a scrollback frame from a grid captured by `scroll_snapshot`.
    /// Run outside the emulator lock. Hide the cursor in the history view.
    pub fn frame_from_grid(grid: Vec<Vec<Slot>>, _cols: u16, rows: u16, title: String) -> Frame {
        let lines: Vec<Arc<str>> = grid.iter().map(|r| Arc::from(serialize_row(r))).collect();
        let row_hashes = lines.iter().map(hash_row).collect::<Vec<_>>();
        Frame {
            content_hash: hash_rows(&row_hashes),
            activity_hash: hash_rows(
                &grid
                    .iter()
                    .map(|row| activity_row_hash(row))
                    .collect::<Vec<_>>(),
            ),
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
            let Ok(row) = usize::try_from(row) else {
                continue;
            };
            if row >= rows {
                continue;
            }
            let cell = ind.cell;
            if let Some(slot) = grid.get_mut(row).and_then(|line| line.get_mut(col)) {
                *slot = slot_from_cell(cell);
            }
        }
        grid
    }

    fn render_frame(&mut self, hide_cursor: bool) -> Frame {
        let damage = match self.term.damage() {
            TermDamage::Full => Damage::Full,
            TermDamage::Partial(bounds) => Damage::Partial(bounds.collect()),
        };
        let rows = self.rows as usize;
        let (offset, display_offset, mode, cursor, alt_screen) = {
            let content = self.term.renderable_content();
            let display_offset = content.display_offset;
            let offset = display_offset as i32;
            let mode = content.mode;
            let cursor = content.cursor;
            let alt_screen = mode.contains(TermMode::ALT_SCREEN);
            (offset, display_offset, mode, cursor, alt_screen)
        };
        let full = matches!(&damage, Damage::Full)
            || !self.render_cache.valid
            || self.render_cache.cols != self.cols
            || self.render_cache.rows != self.rows
            || self.render_cache.display_offset != display_offset
            || self.render_cache.alt_screen != Some(alt_screen)
            || display_offset != 0;

        let (serialized_rows, cells_inspected) = if full {
            crate::perf::count("frame-full-renders", 1);
            (self.rebuild_render_cache(display_offset, alt_screen), 0)
        } else if let Damage::Partial(bounds) = damage {
            self.update_partial_render_cache(bounds, display_offset, alt_screen)
        } else {
            (0, 0)
        };
        if serialized_rows > 0 {
            crate::perf::count("frame-rows-serialized", serialized_rows);
        }
        if cells_inspected > 0 {
            crate::perf::count("frame-cells-inspected", cells_inspected);
        }
        self.term.reset_damage();

        let crow = cursor.point.line.0 + offset;
        let hidden = hide_cursor
            || cursor.shape == CursorShape::Hidden
            || !mode.contains(TermMode::SHOW_CURSOR)
            || crow < 0
            || usize::try_from(crow).map_or(true, |crow| crow >= rows);
        let (cx, cy) = if hidden {
            (0u16, self.rows)
        } else {
            (
                cursor.point.column.0 as u16,
                u16::try_from(crow).unwrap_or_default(),
            )
        };

        let cursor_shape = match cursor.shape {
            CursorShape::Underline => 1,
            CursorShape::Beam => 2,
            _ => 0,
        };

        Frame {
            content_hash: self.render_cache.content_hash,
            activity_hash: hash_rows(&self.render_cache.activity_row_hashes),
            lines: self.render_cache.lines.clone(),
            history: self.history_extent(),
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

    fn update_partial_render_cache(
        &mut self,
        bounds: Vec<LineDamageBounds>,
        display_offset: usize,
        alt_screen: bool,
    ) -> (u64, u64) {
        crate::perf::count("frame-partial-renders", 1);
        let rows = self.rows as usize;
        let cols = self.cols as usize;
        let mut serialized_rows = 0u64;
        let mut cells_inspected = 0u64;
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
                cells_inspected += 1;
                let point = Point::new(Line(row as i32 - display_offset as i32), Column(col));
                let current = slot_from_cell(&self.term.grid()[point]);
                let Some(cached_row) = self.render_cache.cells.get_mut(row) else {
                    self.render_cache.invalidate();
                    break;
                };
                let Some(cached_cell) = cached_row.get_mut(col) else {
                    self.render_cache.invalidate();
                    break;
                };
                if *cached_cell != current {
                    *cached_cell = current;
                    if let Some(row_changed) = changed.get_mut(row) {
                        *row_changed = true;
                    } else {
                        self.render_cache.invalidate();
                        break;
                    }
                }
                if !self.render_cache.valid {
                    break;
                }
            }
        }
        if !self.render_cache.valid {
            crate::perf::count("frame-full-renders", 1);
            serialized_rows += self.rebuild_render_cache(display_offset, alt_screen);
        } else {
            for (((row_changed, cells), row_hash), (activity_hash, cached_line)) in changed
                .into_iter()
                .zip(&self.render_cache.cells)
                .zip(&mut self.render_cache.row_hashes)
                .zip(
                    self.render_cache
                        .activity_row_hashes
                        .iter_mut()
                        .zip(&mut self.render_cache.lines),
                )
            {
                if row_changed {
                    let line: Arc<str> = Arc::from(serialize_row(cells));
                    serialized_rows += 1;
                    *row_hash = hash_row(&line);
                    *activity_hash = activity_row_hash(cells);
                    *cached_line = line;
                }
            }
            self.render_cache.content_hash = hash_rows(&self.render_cache.row_hashes);
        }
        (serialized_rows, cells_inspected)
    }

    fn rebuild_render_cache(&mut self, display_offset: usize, alt_screen: bool) -> u64 {
        let grid = self.capture_visible_grid();
        let mut lines = Vec::with_capacity(self.rows as usize);
        let mut row_hashes = Vec::with_capacity(self.rows as usize);
        for row in &grid {
            let line: Arc<str> = Arc::from(serialize_row(row));
            row_hashes.push(hash_row(&line));
            lines.push(line);
        }
        self.render_cache = RenderCache {
            cols: self.cols,
            rows: self.rows,
            activity_row_hashes: grid.iter().map(|row| activity_row_hash(row)).collect(),
            cells: grid,
            lines,
            content_hash: hash_rows(&row_hashes),
            row_hashes,
            display_offset,
            alt_screen: Some(alt_screen),
            valid: true,
        };
        self.rows as u64
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
        Slot::Ch(
            cell.c,
            cell.zerowidth()
                .filter(|marks| !marks.is_empty())
                .map(Arc::from),
            cell.fg,
            cell.bg,
            cell.flags,
            cell.hyperlink(),
        )
    }
}

// Codex's prompt particles are single-dot Braille glyphs in near-background gray.
// They arrive as ordinary cell writes, not cursor events. Normalize only that narrow
// decoration for activity. The presentation hash and rendered rows remain lossless.
fn activity_row_hash(row: &[Slot]) -> u64 {
    let normalized: Vec<Slot> = row
        .iter()
        .map(|slot| match slot {
            Slot::Ch(c, marks, fg, bg, flags, link)
                if link.is_none()
                    && marks.is_none()
                    && flags.difference(Flags::WRAPLINE).is_empty()
                    && (*c == ' ' || faint_particle(*c, *fg, *bg)) =>
            {
                Slot::Ch(
                    ' ',
                    None,
                    Color::Named(alacritty_terminal::vte::ansi::NamedColor::Foreground),
                    *bg,
                    Flags::empty(),
                    None,
                )
            }
            _ => slot.clone(),
        })
        .collect();
    hash_row(&Arc::from(serialize_row(&normalized)))
}

fn faint_particle(c: char, fg: Color, bg: Color) -> bool {
    // Live captures used gray 13..28 against gray 30. Keep the tolerance below
    // one tenth of the channel range. Normal high-contrast Braille remains text.
    let dot = (c as u32).wrapping_sub(0x2800);
    if dot > 0x80 || !dot.is_power_of_two() {
        return false;
    }
    match (fg, bg) {
        (Color::Spec(fg), Color::Spec(bg)) => {
            fg.r == fg.g
                && fg.g == fg.b
                && bg.r == bg.g
                && bg.g == bg.b
                && fg.r.abs_diff(bg.r) <= 24
        }
        _ => false,
    }
}

fn hash_row(row: &Arc<str>) -> u64 {
    let mut h = std::collections::hash_map::DefaultHasher::new();
    row.hash(&mut h);
    h.finish()
}

fn hash_rows(rows: &[u64]) -> u64 {
    let _perf = crate::perf::timer("frame-hash");
    let mut h = std::collections::hash_map::DefaultHasher::new();
    rows.hash(&mut h);
    h.finish()
}

pub(crate) fn benchmark_content_hash(lines: &[Arc<str>]) -> u64 {
    let row_hashes = lines.iter().map(hash_row).collect::<Vec<_>>();
    hash_rows(&row_hashes)
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
    /// The second half of a wide character. It produces no output.
    Spacer,
    Ch(
        char,
        Option<Arc<[char]>>,
        Color,
        Color,
        Flags,
        Option<Hyperlink>,
    ),
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
    if utf8
        && v > 127
        && let Some(c) = char::from_u32(v)
    {
        let mut buf = [0u8; 4];
        out.extend_from_slice(c.encode_utf8(&mut buf).as_bytes());
        return;
    }
    out.push(v.min(255) as u8);
}

#[cfg(test)]
mod tests;
