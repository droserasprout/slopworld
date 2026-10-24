//! Preserve tmux erase and cluster semantics while bounding retained cell tails.

use super::{RenderCache, SideSink};
use alacritty_terminal::grid::Dimensions;
use alacritty_terminal::index::Column;
use alacritty_terminal::index::Line;
use alacritty_terminal::term::Term;
use alacritty_terminal::term::cell::{CellExtra, Flags};
use alacritty_terminal::vte::ansi::cursor_icon::CursorIcon;
use alacritty_terminal::vte::ansi::*;
use std::sync::Arc;
use unicode_width::{UnicodeWidthChar, UnicodeWidthStr};

// Grid and history limits do not bound an individual cell's combining tail.
pub(super) const MAX_CELL_MARKS: usize = 64;

// Keep parsing discarded scalars without extending the cell's rendered prefix.
// Cell identity prevents this state from surviving an overwrite or grid reset.
pub(super) struct OverflowTail {
    cell: Arc<CellExtra>,
    follows_joiner: bool,
}

pub(super) struct Mirror<'a> {
    pub term: &'a mut Term<SideSink>,
    pub cache: &'a mut RenderCache,
    pub history_dirty: &'a mut bool,
    pub redraw_clear: &'a mut bool,
    pub pager_prompt: &'a mut bool,
    pub overflow_tail: &'a mut Option<OverflowTail>,
}

macro_rules! forward {
    ($($name:ident($($arg:ident: $ty:ty),*);)*) => {
        $(fn $name(&mut self, $($arg: $ty),*) {
            *self.pager_prompt = false;
            self.term.$name($($arg),*);
        })*
    };
}

impl Handler for Mirror<'_> {
    fn clear_screen(&mut self, mode: ClearMode) {
        *self.pager_prompt = false;
        *self.history_dirty = true;
        // less homes the cursor and uses ED 0 before redrawing horizontally scrolled
        // content. tmux may split the replacement into several %output records.
        if matches!(mode, ClearMode::All)
            || (matches!(mode, ClearMode::Below)
                && self.term.grid().cursor.point.line == Line(0)
                && self.term.grid().cursor.point.column == Column(0))
        {
            *self.redraw_clear = true;
        }
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
        *self.pager_prompt = false;
        *self.history_dirty = true;
        if !self.try_input_cluster_component(c) {
            // Alacritty attaches zero-width scalars to column zero at the left
            // margin, and to the base of wide cells elsewhere.
            if c.width() == Some(0) {
                let column = previous_base_column(self.term).unwrap_or(Column(0));
                let line = self.term.grid().cursor.point.line;
                if self.term.grid()[line][column]
                    .zerowidth()
                    .unwrap_or_default()
                    .len()
                    >= MAX_CELL_MARKS
                {
                    self.discard_cluster_component(column, c);
                    return;
                }
            }
            self.term.input(c);
        }
    }
    fn linefeed(&mut self) {
        *self.pager_prompt = false;
        *self.history_dirty = true;
        self.term.linefeed();
    }
    fn newline(&mut self) {
        *self.pager_prompt = false;
        *self.history_dirty = true;
        self.term.newline();
    }
    fn scroll_up(&mut self, count: usize) {
        *self.pager_prompt = false;
        *self.history_dirty = true;
        self.term.scroll_up(count);
    }
    fn scroll_down(&mut self, count: usize) {
        *self.pager_prompt = false;
        *self.history_dirty = true;
        self.term.scroll_down(count);
    }
    fn insert_blank_lines(&mut self, count: usize) {
        *self.pager_prompt = false;
        *self.history_dirty = true;
        self.term.insert_blank_lines(count);
    }
    fn delete_lines(&mut self, count: usize) {
        *self.pager_prompt = false;
        *self.history_dirty = true;
        self.term.delete_lines(count);
    }
    fn reverse_index(&mut self) {
        *self.pager_prompt = false;
        *self.history_dirty = true;
        self.term.reverse_index();
    }
    fn reset_state(&mut self) {
        *self.pager_prompt = false;
        *self.history_dirty = true;
        self.term.reset_state();
    }
    fn set_private_mode(&mut self, mode: PrivateMode) {
        *self.pager_prompt = false;
        *self.history_dirty |= matches!(mode.raw(), 3 | 47 | 1047 | 1049);
        self.term.set_private_mode(mode);
    }
    fn unset_private_mode(&mut self, mode: PrivateMode) {
        *self.pager_prompt = false;
        *self.history_dirty |= matches!(mode.raw(), 3 | 47 | 1047 | 1049);
        self.term.unset_private_mode(mode);
    }

    fn clear_line(&mut self, mode: LineClearMode) {
        // The prompt is a terminal operation, not a particular prompt string.
        // Any subsequent terminal operation invalidates this hint, including a
        // newer clear in the same tmux record, so it cannot complete the next page.
        let cursor = &self.term.grid().cursor;
        *self.pager_prompt = matches!(mode, LineClearMode::Right)
            && cursor.point.line == self.term.bottommost_line()
            && cursor.point.column > Column(0);
        self.term.clear_line(mode);
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
    fn discard_cluster_component(&mut self, column: Column, c: char) {
        let line = self.term.grid().cursor.point.line;
        *self.overflow_tail =
            self.term.grid()[line][column]
                .extra
                .as_ref()
                .map(|cell| OverflowTail {
                    cell: cell.clone(),
                    follows_joiner: c == '\u{200d}',
                });
    }

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
                if self.term.grid()[line][column]
                    .zerowidth()
                    .unwrap_or_default()
                    .len()
                    >= MAX_CELL_MARKS
                {
                    self.discard_cluster_component(column, c);
                    return true;
                }
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
            let follows_joiner = self
                .overflow_tail
                .as_ref()
                .filter(|tail| {
                    cell.extra
                        .as_ref()
                        .is_some_and(|extra| Arc::ptr_eq(extra, &tail.cell))
                })
                .map_or_else(
                    || marks.last() == Some(&'\u{200d}'),
                    |tail| tail.follows_joiner,
                );
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
                    if marks.len() >= MAX_CELL_MARKS {
                        self.discard_cluster_component(column, c);
                        return true;
                    }
                    self.term.grid_mut()[line][column].push_zerowidth(c);
                    if width == 2 && !wide {
                        promote_to_wide(self.term, line, column);
                    }
                    self.cache.invalidate();
                    return true;
                }
                if width_mark && width == 2 && !wide {
                    if marks.len() >= MAX_CELL_MARKS {
                        self.discard_cluster_component(column, c);
                        return true;
                    }
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
