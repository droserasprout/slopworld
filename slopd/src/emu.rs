//! Real server-side terminal emulator.
//!
//! One `SessionEmu` per running session drives the actual `alacritty_terminal`
//! VT engine off the raw byte stream tmux control mode gives us. It replaces the
//! old `capture-pane` screen-scraper: instead of reading a pre-rendered screen we
//! own the grid, cursor and modes, and serialize back into the same SGR-coloured
//! line wire format the mod already speaks (Phase 1: mod unchanged).

use alacritty_terminal::event::VoidListener;
use alacritty_terminal::grid::{Dimensions, Scroll};
use alacritty_terminal::term::cell::Flags;
use alacritty_terminal::term::{Config, Term, TermMode};
use alacritty_terminal::vte::ansi::{Color, CursorShape, Processor};

/// Our own `Dimensions` so we don't depend on the test-gated `TermSize`.
/// History depth comes from `Config.scrolling_history`, not `total_lines`.
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

/// A rendered screen: SGR-coloured rows plus the cursor cell. `cy == rows` hides
/// the cursor (off-screen, invisible, or scrolled into history).
pub struct Frame {
    pub lines: Vec<String>,
    pub cx: u16,
    pub cy: u16,
}

/// One live terminal: the VT engine plus the parser feeding it.
pub struct SessionEmu {
    term: Term<VoidListener>,
    parser: Processor,
    cols: u16,
    rows: u16,
}

impl SessionEmu {
    pub fn new(cols: u16, rows: u16) -> Self {
        let dims = Dims {
            cols: cols as usize,
            rows: rows as usize,
        };
        let term = Term::new(Config::default(), &dims, VoidListener);
        Self {
            term,
            parser: Processor::new(),
            cols,
            rows,
        }
    }

    /// Advance the VT engine over a chunk of raw pty bytes.
    pub fn feed(&mut self, bytes: &[u8]) {
        self.parser.advance(&mut self.term, bytes);
    }

    pub fn resize(&mut self, cols: u16, rows: u16) {
        if cols == self.cols && rows == self.rows {
            return;
        }
        self.cols = cols;
        self.rows = rows;
        self.term.resize(Dims {
            cols: cols as usize,
            rows: rows as usize,
        });
    }

    /// The live bottom frame.
    pub fn render(&self) -> Frame {
        self.render_frame(false)
    }

    /// A one-off frame scrolled `off` lines up into the scrollback, for a wheel
    /// request. Restores the live view afterwards; the cursor is always hidden in
    /// a history view. Returns the offset actually reached (clamped to history).
    pub fn scroll_snapshot(&mut self, off: u16) -> (Frame, u16) {
        let cur = self.term.grid().display_offset() as i32;
        self.term.scroll_display(Scroll::Delta(off as i32 - cur));
        let achieved = self.term.grid().display_offset() as u16;
        let mut frame = self.render_frame(true);
        self.term.scroll_display(Scroll::Bottom);
        frame.cx = 0;
        frame.cy = self.rows;
        (frame, achieved)
    }

    fn render_frame(&self, hide_cursor: bool) -> Frame {
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
            if cell.flags.contains(Flags::WIDE_CHAR_SPACER) {
                grid[row][col] = Slot::Spacer;
            } else {
                grid[row][col] = Slot::Ch(cell.c, cell.fg, cell.bg, cell.flags);
            }
        }

        let lines = grid.iter().map(|r| serialize_row(r)).collect();

        let cur = content.cursor;
        let crow = cur.point.line.0 + offset;
        let hidden = hide_cursor
            || cur.shape == CursorShape::Hidden
            || !content.mode.contains(TermMode::SHOW_CURSOR)
            || crow < 0
            || crow as usize >= rows;
        let (cx, cy) = if hidden {
            (0u16, self.rows)
        } else {
            (cur.point.column.0 as u16, crow as u16)
        };

        Frame { lines, cx, cy }
    }
}

/// One grid cell in a rendered row.
enum Slot {
    /// Never-touched cell: a default-attribute space.
    Blank,
    /// The second half of a wide char; produces no output of its own.
    Spacer,
    Ch(char, Color, Color, Flags),
}

/// Serializes one row into the SGR wire format the mod parses. Each line opens
/// with a reset, self-contained SGR runs colour each stretch, and trailing
/// default cells are trimmed just like `capture-pane` did.
fn serialize_row(row: &[Slot]) -> String {
    let mut out = String::from("\x1b[0m");

    let last = row.iter().rposition(|s| match s {
        Slot::Ch(c, fg, bg, flags) => {
            !(*c == ' ' && fg_code(*fg).is_none() && bg_code(*bg).is_none() && flags.is_empty())
        }
        Slot::Spacer | Slot::Blank => false,
    });
    let Some(last) = last else { return out };

    let mut cur_sgr = String::new();
    for slot in &row[..=last] {
        match slot {
            Slot::Spacer => {}
            Slot::Blank => {
                if !cur_sgr.is_empty() {
                    out.push_str("\x1b[0m");
                    cur_sgr.clear();
                }
                out.push(' ');
            }
            Slot::Ch(c, fg, bg, flags) => {
                let sgr = sgr_for(*fg, *bg, *flags);
                if sgr != cur_sgr {
                    out.push_str(if sgr.is_empty() { "\x1b[0m" } else { &sgr });
                    cur_sgr = sgr;
                }
                out.push(*c);
            }
        }
    }
    out
}

/// A self-contained SGR sequence for one cell, or "" when it needs no attributes.
/// The mod only renders bold, reverse and colour, so that's all we emit.
fn sgr_for(fg: Color, bg: Color, flags: Flags) -> String {
    let mut params: Vec<String> = Vec::new();
    if flags.contains(Flags::BOLD) {
        params.push("1".into());
    }
    if flags.contains(Flags::INVERSE) {
        params.push("7".into());
    }
    if let Some(code) = fg_code(fg) {
        params.push(code);
    }
    if let Some(code) = bg_code(bg) {
        params.push(code);
    }
    if params.is_empty() {
        return String::new();
    }
    format!("\x1b[0;{}m", params.join(";"))
}

/// Emits the *same* index-based SGR codes tmux would, not resolved RGB, so the
/// mod's curated palette keeps its muted look. `None` means the default colour.
fn fg_code(c: Color) -> Option<String> {
    match c {
        Color::Named(n) => match n as usize {
            i if i <= 7 => Some((30 + i).to_string()),
            i if i <= 15 => Some((90 + (i - 8)).to_string()),
            _ => None,
        },
        Color::Indexed(i) => Some(match i {
            0..=7 => (30 + i as u16).to_string(),
            8..=15 => (90 + (i as u16 - 8)).to_string(),
            _ => format!("38;5;{i}"),
        }),
        Color::Spec(rgb) => Some(format!("38;2;{};{};{}", rgb.r, rgb.g, rgb.b)),
    }
}

fn bg_code(c: Color) -> Option<String> {
    match c {
        Color::Named(n) => match n as usize {
            i if i <= 7 => Some((40 + i).to_string()),
            i if i <= 15 => Some((100 + (i - 8)).to_string()),
            _ => None,
        },
        Color::Indexed(i) => Some(match i {
            0..=7 => (40 + i as u16).to_string(),
            8..=15 => (100 + (i as u16 - 8)).to_string(),
            _ => format!("48;5;{i}"),
        }),
        Color::Spec(rgb) => Some(format!("48;2;{};{};{}", rgb.r, rgb.g, rgb.b)),
    }
}

// -------------------------------------------------------------- control mode

/// Extracts the unescaped payload bytes of a `%output` control-mode line, or
/// `None` for any other notification. Line shape: `%output %<pane> <data>`.
pub fn parse_output(line: &str) -> Option<Vec<u8>> {
    let rest = line.strip_prefix("%output ")?;
    let sp = rest.find(' ')?;
    Some(unescape(&rest[sp + 1..]))
}

/// Reverses tmux's control-mode escaping: printable bytes are literal, `\\` is a
/// backslash, and everything else is a 3-digit octal escape (`\NNN`).
pub fn unescape(s: &str) -> Vec<u8> {
    let b = s.as_bytes();
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
            unescape("\\015\\012\\033[?2004l\\015"),
            b"\r\n\x1b[?2004l\r"
        );
        assert_eq!(unescape("a\\\\b"), b"a\\b");
        assert_eq!(unescape("hi"), b"hi");
    }

    #[test]
    fn parses_output_line() {
        assert_eq!(parse_output("%output %0 \\015hi").unwrap(), b"\rhi");
        assert!(parse_output("%exit").is_none());
        assert!(parse_output("%session-changed $0 name").is_none());
    }

    #[test]
    fn renders_plain_text() {
        let mut e = SessionEmu::new(20, 3);
        e.feed(b"hello");
        let f = e.render();
        assert_eq!(f.lines.len(), 3);
        assert_eq!(f.lines[0], "\x1b[0mhello");
        // cursor sits just past the text on row 0.
        assert_eq!((f.cx, f.cy), (5, 0));
    }

    #[test]
    fn maps_colour_to_index_sgr() {
        let mut e = SessionEmu::new(20, 2);
        // bold + red fg
        e.feed(b"\x1b[1;31mX\x1b[0m");
        let f = e.render();
        assert_eq!(f.lines[0], "\x1b[0m\x1b[0;1;31mX");
    }

    #[test]
    fn trims_trailing_default_cells() {
        let mut e = SessionEmu::new(20, 2);
        e.feed(b"ab");
        let f = e.render();
        assert_eq!(f.lines[0], "\x1b[0mab");
        // untouched second row is just the reset.
        assert_eq!(f.lines[1], "\x1b[0m");
    }
}
