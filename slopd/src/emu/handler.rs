//! Preserve tmux erase semantics while forwarding the remaining VT operations.

use super::{RenderCache, SideSink};
use alacritty_terminal::index::Column;
use alacritty_terminal::index::Line;
use alacritty_terminal::term::cell::Flags;
use alacritty_terminal::term::Term;
use alacritty_terminal::vte::ansi::cursor_icon::CursorIcon;
use alacritty_terminal::vte::ansi::*;
use unicode_width::UnicodeWidthStr;

pub(super) struct Mirror<'a> {
    pub term: &'a mut Term<SideSink>,
    pub cache: &'a mut RenderCache,
    pub history_dirty: &'a mut bool,
}

macro_rules! forward {
    ($($name:ident($($arg:ident: $ty:ty),*);)*) => {
        $(fn $name(&mut self, $($arg: $ty),*) {
            self.term.$name($($arg),*);
        })*
    };
}

impl Handler for Mirror<'_> {
    fn clear_screen(&mut self, mode: ClearMode) {
        *self.history_dirty = true;
        match mode {
            // Alacritty preserves the erased viewport in scrollback. tmux erases it
            // in place. Preserving it here creates history on every TUI startup/redraw.
            ClearMode::All => {
                self.term.grid_mut().reset_region(..);
                self.term.selection = None;
                self.cache.invalidate();
            }
            ClearMode::Above => {
                // Alacritty 0.26 skips row zero when the cursor is on row one.
                if self.term.grid().cursor.point.line == Line(1) {
                    self.term.grid_mut().reset_region(..Line(1));
                    self.cache.invalidate();
                }
                self.term.clear_screen(mode);
            }
            _ => self.term.clear_screen(mode),
        }
    }

    fn input(&mut self, c: char) {
        *self.history_dirty = true;
        if !self.try_input_cluster_component(c) {
            self.term.input(c);
        }
    }
    fn linefeed(&mut self) {
        *self.history_dirty = true;
        self.term.linefeed();
    }
    fn newline(&mut self) {
        *self.history_dirty = true;
        self.term.newline();
    }
    fn scroll_up(&mut self, count: usize) {
        *self.history_dirty = true;
        self.term.scroll_up(count);
    }
    fn scroll_down(&mut self, count: usize) {
        *self.history_dirty = true;
        self.term.scroll_down(count);
    }
    fn insert_blank_lines(&mut self, count: usize) {
        *self.history_dirty = true;
        self.term.insert_blank_lines(count);
    }
    fn delete_lines(&mut self, count: usize) {
        *self.history_dirty = true;
        self.term.delete_lines(count);
    }
    fn reverse_index(&mut self) {
        *self.history_dirty = true;
        self.term.reverse_index();
    }
    fn reset_state(&mut self) {
        *self.history_dirty = true;
        self.term.reset_state();
    }
    fn set_private_mode(&mut self, mode: PrivateMode) {
        *self.history_dirty |= matches!(mode.raw(), 3 | 47 | 1047 | 1049);
        self.term.set_private_mode(mode);
    }
    fn unset_private_mode(&mut self, mode: PrivateMode) {
        *self.history_dirty |= matches!(mode.raw(), 3 | 47 | 1047 | 1049);
        self.term.unset_private_mode(mode);
    }

    forward! {
        set_title(title: Option<String>);
        set_cursor_style(style: Option<CursorStyle>);
        set_cursor_shape(shape: CursorShape);
        goto(line: i32, col: usize);
        goto_line(line: i32);
        goto_col(col: usize);
        insert_blank(count: usize);
        move_up(count: usize);
        move_down(count: usize);
        identify_terminal(intermediate: Option<char>);
        device_status(status: usize);
        move_forward(count: usize);
        move_backward(count: usize);
        move_down_and_cr(count: usize);
        move_up_and_cr(count: usize);
        put_tab(count: u16);
        backspace();
        carriage_return();
        bell();
        substitute();
        set_horizontal_tabstop();
        erase_chars(count: usize);
        delete_chars(count: usize);
        move_backward_tabs(count: u16);
        move_forward_tabs(count: u16);
        save_cursor_position();
        restore_cursor_position();
        clear_line(mode: LineClearMode);
        clear_tabs(mode: TabulationClearMode);
        set_tabs(interval: u16);
        terminal_attribute(attr: Attr);
        set_mode(mode: Mode);
        unset_mode(mode: Mode);
        report_mode(mode: Mode);
        report_private_mode(mode: PrivateMode);
        set_scrolling_region(top: usize, bottom: Option<usize>);
        set_keypad_application_mode();
        unset_keypad_application_mode();
        set_active_charset(index: CharsetIndex);
        configure_charset(index: CharsetIndex, charset: StandardCharset);
        set_color(index: usize, color: Rgb);
        dynamic_color_sequence(prefix: String, index: usize, terminator: &str);
        reset_color(index: usize);
        clipboard_store(clipboard: u8, text: &[u8]);
        clipboard_load(clipboard: u8, terminator: &str);
        decaln();
        push_title();
        pop_title();
        text_area_size_pixels();
        text_area_size_chars();
        set_hyperlink(link: Option<Hyperlink>);
        set_mouse_cursor_icon(icon: CursorIcon);
        report_keyboard_mode();
        push_keyboard_mode(mode: KeyboardModes);
        pop_keyboard_modes(count: u16);
        set_keyboard_mode(mode: KeyboardModes, behavior: KeyboardModesApplyBehavior);
        set_modify_other_keys(mode: ModifyOtherKeys);
        report_modify_other_keys();
        set_scp(path: ScpCharPath, mode: ScpUpdateMode);
    }
}

impl Mirror<'_> {
    // Consume tmux cluster components; ordinary characters use Term::input.
    fn try_input_cluster_component(&mut self, c: char) -> bool {
        // ASCII cannot extend any of the tmux emoji clusters handled below.
        if c.is_ascii() {
            return false;
        }
        // Match the private tmux server's codepoint-widths override even outside
        // emoji sequences. At the left margin tmux discards an unattached modifier.
        if ('\u{1f3fb}'..='\u{1f3ff}').contains(&c) {
            if let Some(column) = previous_base_column(self.term) {
                let line = self.term.grid().cursor.point.line;
                self.term.grid_mut()[line][column].push_zerowidth(c);
                self.cache.invalidate();
            }
            return true;
        }
        // Alacritty measures each scalar separately. tmux instead keeps emoji
        // sequences in one cell and sometimes widens that cell when a selector,
        // keycap mark, or second regional indicator arrives.
        if let Some(column) = previous_base_column(self.term) {
            let line = self.term.grid().cursor.point.line;
            let cell = &self.term.grid()[line][column];
            let marks = cell.zerowidth().unwrap_or_default();
            let follows_joiner = marks.last() == Some(&'\u{200d}');
            let regional_pair = (0x1f1e6..=0x1f1ff).contains(&(cell.c as u32))
                && (0x1f1e6..=0x1f1ff).contains(&(c as u32))
                && marks.is_empty();
            let width_mark = matches!(c, '\u{fe0f}' | '\u{20e3}');
            if (follows_joiner || regional_pair || width_mark) && cell.c != ' ' {
                let mut candidate = String::from(cell.c);
                candidate.extend(marks);
                candidate.push(c);
                let wide = cell.flags.contains(Flags::WIDE_CHAR);
                let width = UnicodeWidthStr::width(candidate.as_str());
                // The joined part may be an arrow or gender symbol. The first
                // cell's emoji base identifies the sequence, including prefixes
                // that need a later VS16 before they measure two columns.
                let emoji_base = matches!(cell.c as u32, 0x2000..=0x27ff | 0x1f000..=0x1ffff);
                let emoji_part = matches!(c as u32,
                    0x2194 | 0x2195 | 0x2b1b | 0x2600..=0x27ff | 0x1f000..=0x1ffff);
                let joined_emoji = follows_joiner && emoji_base && emoji_part;
                if joined_emoji || (regional_pair && width == 2) {
                    self.term.grid_mut()[line][column].push_zerowidth(c);
                    if width == 2 && !wide {
                        promote_to_wide(self.term, line, column);
                    }
                    self.cache.invalidate();
                    return true;
                }
                if width_mark && width == 2 && !wide {
                    self.term.input(c);
                    promote_to_wide(self.term, line, column);
                    self.cache.invalidate();
                    return true;
                }
            }
        }
        false
    }
}

fn previous_base_column(term: &Term<SideSink>) -> Option<Column> {
    let cursor = &term.grid().cursor;
    let line = cursor.point.line;
    let mut column = cursor.point.column;
    if !cursor.input_needs_wrap {
        column.0 = column.0.checked_sub(1)?;
    }
    if term.grid()[line][column]
        .flags
        .contains(Flags::WIDE_CHAR_SPACER)
    {
        column.0 = column.0.checked_sub(1)?;
    }
    Some(column)
}

/// Turn a just-written narrow cell into a wide one when tmux's grapheme width
/// increases. The cursor is still immediately after its base cell.
fn promote_to_wide(term: &mut Term<SideSink>, line: Line, column: Column) {
    let cursor = &term.grid().cursor;
    if cursor.input_needs_wrap || cursor.point.line != line || cursor.point.column.0 != column.0 + 1
    {
        return;
    }
    let spacer = cursor.point.column;
    term.input(' ');
    term.grid_mut()[line][column].flags.insert(Flags::WIDE_CHAR);
    term.grid_mut()[line][spacer]
        .flags
        .insert(Flags::WIDE_CHAR_SPACER);
}
