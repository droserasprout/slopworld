//! Server-side terminal emulator: one `SessionEmu` per running session, driving an
//! `alacritty_terminal` VT engine off the raw bytes tmux control mode gives us. We
//! own the grid, cursor and modes, and serialize back into the SGR-coloured line
//! format the mod speaks.

use std::sync::{Arc, Mutex};

use alacritty_terminal::event::{Event, EventListener};
use alacritty_terminal::grid::{Dimensions, Scroll};
use alacritty_terminal::term::cell::{Flags, Hyperlink};
use alacritty_terminal::term::{ClipboardType, Config, Term, TermMode};
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

/// `cy == rows` hides the cursor (off-screen, invisible, or scrolled into
/// history). Rows carry `\x1b[<n>G` (CHA) markers ahead of any run whose true
/// column diverges from the natural pen position - which is exactly where a wide
/// char skipped a cell - and the mod redraws those runs at their absolute column.
pub struct Frame {
    pub lines: Vec<String>,
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
    /// What the app called itself (OSC 0/2); empty until it says, and empty again
    /// when it resets.
    pub title: String,
}

/// Everything the VT engine hands *back* rather than draws.
#[derive(Default)]
struct Side {
    /// Answers owed to the pane, written back down the pty.
    replies: Vec<u8>,
    /// The last OSC 52 store. One slot rather than a queue: a clipboard holds one
    /// thing, so an app that copies twice before we drain has only ever meant the
    /// second.
    clip: Option<String>,
    title: Option<String>,
}

/// Replies to cursor-position reports (`ESC[6n`), device attributes and mode
/// queries. `VoidListener` dropped all of these, which left apps that probe the
/// terminal (Ink, which Claude Code is built on) waiting on a report that never
/// came and anchoring their cursor on the wrong line.
///
/// It also takes the two things an app says *about* itself rather than to itself:
/// its title, and a clipboard write. Only the store half of OSC 52 is here - the
/// default `Osc52::OnlyCopy` has `Term` refuse a load, which is the right way
/// round when the app is an agent and the clipboard is the operator's.
#[derive(Clone)]
struct ReplySink {
    side: Arc<Mutex<Side>>,
}

impl EventListener for ReplySink {
    fn send_event(&self, event: Event) {
        let Ok(mut s) = self.side.lock() else { return };
        match event {
            Event::PtyWrite(text) => s.replies.extend_from_slice(text.as_bytes()),
            Event::ClipboardStore(ClipboardType::Clipboard, text) => s.clip = Some(text),
            Event::Title(t) => s.title = Some(t),
            Event::ResetTitle => s.title = None,
            _ => {}
        }
    }
}

pub struct SessionEmu {
    term: Term<ReplySink>,
    parser: Processor,
    cols: u16,
    rows: u16,
    side: Arc<Mutex<Side>>,
}

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
        let term = Term::new(config, &dims, ReplySink { side: side.clone() });
        Self {
            term,
            parser: Processor::new(),
            cols,
            rows,
            side,
        }
    }

    pub fn feed(&mut self, bytes: &[u8]) {
        self.parser.advance(&mut self.term, bytes);
    }

    /// The caller writes these back into the pane, so the app gets its answer.
    pub fn take_replies(&mut self) -> Vec<u8> {
        match self.side.lock() {
            Ok(mut s) => std::mem::take(&mut s.replies),
            Err(_) => Vec::new(),
        }
    }

    /// The last OSC 52 store, for the caller to put on the host's clipboard.
    /// Left in place if the caller does not ask, so a write in flight never loses
    /// the copy that came after it.
    pub fn take_clip(&mut self) -> Option<String> {
        self.side.lock().ok().and_then(|mut s| s.clip.take())
    }

    fn title(&self) -> String {
        self.side
            .lock()
            .ok()
            .and_then(|s| s.title.clone())
            .unwrap_or_default()
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

    pub fn render(&self) -> Frame {
        self.render_frame(false)
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
                grid[row][col] = Slot::Ch(cell.c, cell.fg, cell.bg, cell.flags, cell.hyperlink());
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
            app_drag: content
                .mode
                .intersects(TermMode::MOUSE_MOTION | TermMode::MOUSE_DRAG),
            alt_screen: content.mode.contains(TermMode::ALT_SCREEN),
            title: self.title(),
        }
    }

    /// DECSET 2004.
    pub fn bracketed_paste(&self) -> bool {
        self.term.mode().contains(TermMode::BRACKETED_PASTE)
    }
}

enum Slot {
    /// Never-touched cell: a default-attribute space.
    Blank,
    /// The second half of a wide char; produces no output of its own.
    Spacer,
    Ch(char, Color, Color, Flags, Option<Hyperlink>),
}

/// Each line opens with a reset, SGR runs are self-contained, and trailing default
/// cells are trimmed. The CHA marker is emitted only where the true column
/// diverges from the pen - right after a wide char - so plain ASCII rows stay
/// byte-identical to a plain capture.
fn serialize_row(row: &[Slot]) -> String {
    let mut out = String::from("\x1b[0m");

    let last = row.iter().rposition(|s| match s {
        Slot::Ch(c, fg, bg, flags, link) => {
            !(*c == ' '
                && fg_code(*fg).is_none()
                && bg_code(*bg).is_none()
                && flags.is_empty()
                && link.is_none())
        }
        Slot::Spacer | Slot::Blank => false,
    });
    let Some(last) = last else { return out };

    let mut cur_sgr = String::new();
    let mut cur_uri = String::new();
    // Where the mod's pen lands with no CHA: one column per emitted char.
    let mut expected = 0usize;
    for (col, slot) in row[..=last].iter().enumerate() {
        let (c, sgr, uri) = match slot {
            Slot::Spacer => continue,
            Slot::Blank => (' ', String::new(), String::new()),
            Slot::Ch(c, fg, bg, flags, link) => (
                *c,
                sgr_for(*fg, *bg, *flags),
                link.as_ref().map(|h| safe_uri(h.uri())).unwrap_or_default(),
            ),
        };
        if col != expected {
            out.push_str(&format!("\x1b[{}G", col + 1));
            expected = col;
        }
        if sgr != cur_sgr {
            out.push_str(if sgr.is_empty() { "\x1b[0m" } else { &sgr });
            cur_sgr = sgr;
        }
        if uri != cur_uri {
            out.push_str(&format!("\x1b]8;;{uri}\x1b\\"));
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

/// The mod renders only bold, reverse and colour, so that's all we emit.
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

/// The *same* index-based SGR codes tmux would send, not resolved RGB, so the
/// mod's curated palette keeps its muted look. `base` is 30 for a foreground, 40
/// for a background.
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


/// Line shape: `%output %<pane> <data>`. Operates on raw bytes, never a `&str`:
/// tmux emits UTF-8 literally and splits its chunks on arbitrary byte boundaries,
/// so a line can end mid-character. The halves flow through to the VT parser,
/// which reassembles across feeds.
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
        assert_eq!(f.lines[0], "\x1b[0mhello");
        assert_eq!((f.cx, f.cy), (5, 0));
        assert_eq!(f.cursor_shape, 0);
        assert!(f.cursor_blink);
    }

    #[test]
    fn maps_colour_to_index_sgr() {
        let mut e = SessionEmu::new(20, 2);
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
        assert_eq!(f.lines[1], "\x1b[0m");
    }

    #[test]
    fn wide_char_emits_cha_for_following_run() {
        let mut e = SessionEmu::new(20, 2);
        // A CJK char occupies two cells, so the run after it must be re-anchored with CHA
        // to column 3.
        e.feed("\u{4f60}X".as_bytes());
        let f = e.render();
        assert_eq!(f.lines[0], "\x1b[0m\u{4f60}\x1b[3GX");
    }

    #[test]
    fn wraps_a_hyperlinked_run_in_osc8() {
        let mut e = SessionEmu::new(20, 2);
        e.feed(b"go \x1b]8;;https://example.com\x1b\\here\x1b]8;;\x1b\\ now");
        let f = e.render();
        assert_eq!(
            f.lines[0],
            "\x1b[0mgo \x1b]8;;https://example.com\x1b\\here\x1b]8;;\x1b\\ now"
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
    fn answers_cursor_position_report() {
        let mut e = SessionEmu::new(20, 5);
        // Move to row 3, col 5 (CUP), then ask for the position (DSR 6).
        e.feed(b"\x1b[3;5H\x1b[6n");
        assert_eq!(e.take_replies(), b"\x1b[3;5R");
        // Draining is one-shot.
        assert!(e.take_replies().is_empty());
    }

    #[test]
    fn answers_primary_device_attributes() {
        let mut e = SessionEmu::new(20, 5);
        e.feed(b"\x1b[c");
        assert_eq!(e.take_replies(), b"\x1b[?6c");
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
