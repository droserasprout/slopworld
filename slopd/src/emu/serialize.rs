//! Encode emulator cells as independently styled terminal rows.

use super::Slot;
use alacritty_terminal::term::cell::Flags;
use alacritty_terminal::vte::ansi::Color;

/// Cell style ready for rendering. Some `Color::Named` values emit no code.
/// Compare normalized pens to avoid formatting strings for each cell.
#[derive(Clone, Copy, PartialEq, Eq, Default)]
struct Pen {
    bold: bool,
    dim: bool,
    inverse: bool,
    fg: Ink,
    bg: Ink,
}

/// A color representation that stores basic colors as offsets from SGR codes `30` and `40`.
#[derive(Clone, Copy, PartialEq, Eq, Default)]
enum Ink {
    /// Emit no code. Use the mod's default foreground or background color.
    #[default]
    Unsaid,
    Basic(u16),
    Indexed(u8),
    Rgb(u8, u8, u8),
}

/// Preserve color indices so the mod can apply its palette.
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
            // Preserve the dim attribute for faint text, such as Claude Code completions.
            // A faint cell stores its full color. The attribute controls dimming.
            dim: flags.contains(Flags::DIM),
            inverse: flags.contains(Flags::INVERSE),
            fg: ink(fg),
            bg: ink(bg),
        }
    }

    /// Write directly to the row buffer in the existing attribute order.
    /// Emit only the attributes that the mod renders: bold, faint, reverse, and color.
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
            write!(out, ";{}", base + off).expect("writing to a String cannot fail");
        }
        Ink::Indexed(i) => {
            write!(out, ";{};5;{i}", base + 8).expect("writing to a String cannot fail");
        }
        Ink::Rgb(r, g, b) => {
            write!(out, ";{};2;{r};{g};{b}", base + 8).expect("writing to a String cannot fail");
        }
    }
}

/// Serialize each row independently. Reset attributes and trim trailing default cells.
/// Emit only necessary CHA, pen, and link updates. Avoid allocating memory for each cell.
pub(super) fn serialize_row(row: &[Slot]) -> String {
    let mut out = String::from("\x1b[0m");

    let last = row.iter().rposition(|s| match s {
        Slot::Ch(c, marks, fg, bg, flags, link) => {
            !(*c == ' '
                && marks.is_none()
                && ink(*fg) == Ink::Unsaid
                && ink(*bg) == Ink::Unsaid
                && flags.is_empty()
                && link.is_none())
        }
        Slot::Spacer | Slot::Blank => false,
    });
    let Some(last) = last else { return out };

    let mut cur_pen = Pen::default();
    // Compare raw URIs. Call `safe_uri` only when the URI changes because it allocates memory.
    // A link can cover multiple cells.
    let mut cur_uri: &str = "";
    // Without CHA, the mod advances one column for each emitted character.
    let mut expected = 0usize;
    for (col, slot) in row
        .get(..=last)
        .expect("last nonblank slot is within the row")
        .iter()
        .enumerate()
    {
        let (c, marks, pen, uri) = match slot {
            Slot::Spacer => continue,
            Slot::Blank => (' ', None, Pen::default(), ""),
            Slot::Ch(c, marks, fg, bg, flags, link) => (
                *c,
                marks.as_deref(),
                Pen::of(*fg, *bg, *flags),
                link.as_ref().map(|h| h.uri()).unwrap_or_default(),
            ),
        };
        if col != expected {
            use std::fmt::Write;
            write!(out, "\x1b[{}G", col + 1).expect("writing to a String cannot fail");
            expected = col;
        }
        if pen != cur_pen {
            pen.write(&mut out);
            cur_pen = pen;
        }
        if uri != cur_uri {
            use std::fmt::Write;
            write!(out, "\x1b]8;;{}\x1b\\", safe_uri(uri))
                .expect("writing to a String cannot fail");
            cur_uri = uri;
        }
        if let Some(marks) = marks {
            // Private row marker: the next scalar count belongs to this one cell.
            // The mod uses the supplied width, so joiners and selectors never
            // become phantom terminal columns.
            use std::fmt::Write;
            let width = if matches!(row.get(col + 1), Some(Slot::Spacer)) {
                2
            } else {
                1
            };
            write!(out, "\x1b[{};{}z", marks.len() + 1, width)
                .expect("writing to a String cannot fail");
            out.push(c);
            out.extend(marks);
            expected += width;
        } else {
            out.push(c);
            expected += 1;
        }
        // Preserve the occupied end even when trimming removes the final spacer.
        // CHA closes a wide glyph on the wire. The client never guesses Unicode widths.
        if matches!(row.get(col + 1), Some(Slot::Spacer)) {
            use std::fmt::Write;
            expected = col + 2;
            write!(out, "\x1b[{}G", expected + 1).expect("writing to a String cannot fail");
        }
    }
    if !cur_uri.is_empty() {
        out.push_str("\x1b]8;;\x1b\\");
    }
    out
}

fn safe_uri(uri: &str) -> String {
    uri.chars().filter(|c| !c.is_control()).take(2048).collect()
}

#[cfg(test)]
#[path = "serialize_tests.rs"]
mod tests;
