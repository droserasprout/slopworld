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
use alacritty_terminal::vte::ansi::{Color, CursorShape, CursorStyle, Processor};

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
/// the cursor (off-screen, invisible, or scrolled into history). Rows carry
/// `\x1b[<n>G` (CHA) column markers ahead of any run whose true column diverges
/// from the natural pen position, which is exactly where a wide char skipped a
/// cell — the mod redraws such runs at their absolute column.
pub struct Frame {
    pub lines: Vec<String>,
    pub cx: u16,
    pub cy: u16,
    /// 0 = block, 1 = underline, 2 = beam.
    pub cursor_shape: u8,
    /// Whether the app wants the cursor to blink.
    pub cursor_blink: bool,
    /// The app is asking for mouse reports (any of click/motion/drag).
    pub app_mouse: bool,
    /// The app is on the alternate screen (no scrollback of its own).
    pub alt_screen: bool,
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
        // Default to a blinking block so unstyled shells keep the familiar blink;
        // apps that set a steady cursor via DECSCUSR still override it.
        let config = Config {
            default_cursor_style: CursorStyle {
                shape: CursorShape::Block,
                blinking: true,
            },
            ..Config::default()
        };
        let term = Term::new(config, &dims, VoidListener);
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

    /// Encodes a mouse event into the report bytes the app expects, per its
    /// current mouse mode (SGR / UTF-8 / legacy). Returns `None` when the app
    /// isn't asking for mouse reports, or for a drag it didn't opt into — the
    /// caller then falls back to local scroll/selection.
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

        let cursor_shape = match cur.shape {
            CursorShape::Underline => 1,
            CursorShape::Beam => 2,
            _ => 0,
        };

        Frame {
            lines,
            cx,
            cy,
            cursor_shape,
            cursor_blink: self.term.cursor_style().blinking,
            app_mouse: content.mode.intersects(TermMode::MOUSE_MODE),
            alt_screen: content.mode.contains(TermMode::ALT_SCREEN),
        }
    }

    /// Whether the app enabled bracketed paste (DECSET 2004).
    pub fn bracketed_paste(&self) -> bool {
        self.term.mode().contains(TermMode::BRACKETED_PASTE)
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
/// default cells are trimmed just like `capture-pane` did. A `\x1b[<n>G` (CHA)
/// marker is emitted only when the true grid column diverges from the natural
/// pen position — i.e. right after a wide char skipped a spacer cell — so plain
/// ASCII rows stay byte-identical to Phase 1 while wide chars keep alignment.
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
    // Where the mod's pen lands with no CHA: one column per emitted char.
    let mut expected = 0usize;
    for (col, slot) in row[..=last].iter().enumerate() {
        let (c, sgr) = match slot {
            Slot::Spacer => continue,
            Slot::Blank => (' ', String::new()),
            Slot::Ch(c, fg, bg, flags) => (*c, sgr_for(*fg, *bg, *flags)),
        };
        if col != expected {
            out.push_str(&format!("\x1b[{}G", col + 1));
            expected = col;
        }
        if sgr != cur_sgr {
            out.push_str(if sgr.is_empty() { "\x1b[0m" } else { &sgr });
            cur_sgr = sgr;
        }
        out.push(c);
        expected += 1;
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
/// `base` is 30 for a foreground, 40 for a background; the bright and 256/RGB
/// selectors offset from there identically for both.
fn color_code(c: Color, base: u16) -> Option<String> {
    match c {
        Color::Named(n) => match n as u16 {
            i if i <= 7 => Some((base + i).to_string()),
            i if i <= 15 => Some((base + 60 + (i - 8)).to_string()),
            _ => None,
        },
        Color::Indexed(i) => Some(match i {
            0..=7 => (base + i as u16).to_string(),
            8..=15 => (base + 60 + (i as u16 - 8)).to_string(),
            _ => format!("{};5;{i}", base + 8),
        }),
        Color::Spec(rgb) => Some(format!("{};2;{};{};{}", base + 8, rgb.r, rgb.g, rgb.b)),
    }
}

fn fg_code(c: Color) -> Option<String> {
    color_code(c, 30)
}

fn bg_code(c: Color) -> Option<String> {
    color_code(c, 40)
}

// --------------------------------------------------------------------- mouse

/// What a mouse event does. `button` (in `MouseInput`) is 0/1/2 = left/mid/right
/// for press/release/drag and ignored for the wheel.
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

/// A mouse event in cell coordinates (0-based), as the mod reports it.
pub struct MouseInput {
    pub action: MouseAction,
    pub button: u8,
    pub col: u16,
    pub row: u16,
}

/// Legacy/UTF-8 coordinate byte: a raw byte, or a UTF-8 char past 127 under 1005.
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
        // Unstyled sessions default to a blinking block.
        assert_eq!(f.cursor_shape, 0);
        assert!(f.cursor_blink);
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

    #[test]
    fn wide_char_emits_cha_for_following_run() {
        let mut e = SessionEmu::new(20, 2);
        // A CJK char occupies two cells; "X" after it sits at column 2, so the
        // run after the wide char must be re-anchored with CHA to column 3 (1-based).
        e.feed("\u{4f60}X".as_bytes());
        let f = e.render();
        assert_eq!(f.lines[0], "\x1b[0m\u{4f60}\x1b[3GX");
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
        // DECSCUSR 6 -> steady bar (beam); enable SGR mouse click reporting.
        e.feed(b"\x1b[6 q\x1b[?1006h\x1b[?1000h");
        let f = e.render();
        assert_eq!(f.cursor_shape, 2);
        assert!(f.app_mouse);
        assert!(!f.alt_screen);
        // 6 is the steady (non-blinking) bar.
        assert!(!f.cursor_blink);
    }

    #[test]
    fn tracks_bracketed_paste_mode() {
        let mut e = SessionEmu::new(20, 2);
        assert!(!e.bracketed_paste());
        e.feed(b"\x1b[?2004h");
        assert!(e.bracketed_paste());
        e.feed(b"\x1b[?2004l");
        assert!(!e.bracketed_paste());
    }
}
