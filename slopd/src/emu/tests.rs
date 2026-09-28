use super::*;

#[test]
fn cursor_updates_reuse_blank_history_extent_until_history_can_change() {
    let mut e = SessionEmu::new(20, 2);
    e.feed(b"\r\n\r\n");
    assert_eq!(e.render().history, 0);
    assert_eq!(e.history_extent_cache, Some(0));
    e.feed(b"\x1b[1;1H");
    assert_eq!(e.history_extent_cache, Some(0));
    assert_eq!(e.render().history, 0);
    e.feed(b"content");
    assert_eq!(e.history_extent_cache, None);
    e.render();
    e.resize(21, 2);
    assert_eq!(e.history_extent_cache, None);
}

#[test]
fn inline_header_insertion_does_not_expose_initial_blank_padding() {
    // Reduced from a recorded fresh Codex startup: insert a six-row header in
    // an eight-row scrolling region, with a blank row before and after it.
    let bytes = b"\x1b[?2026h\x1b[1;8r\x1b[H\r\none\r\ntwo\r\nthree\r\nfour\r\nfive\r\nsix\r\n\r\n\x1b[r\x1b[?2026l";
    let mut e = SessionEmu::new(20, 12);
    for byte in bytes {
        e.feed(std::slice::from_ref(byte));
    }
    assert_eq!(e.history_lines(), 1, "VT padding stays in the raw grid");
    let live = e.render();
    assert_eq!(live.history, 0);
    assert!(live.lines[0].contains("one"));
    let (_, achieved, history, _) = e.scroll_snapshot(1);
    assert_eq!((achieved, history), (0, 0));
    assert_eq!(e.render().content_hash, live.content_hash);

    e.feed(b"\x1b[12;1Htail\r\nnext");
    assert_eq!(e.render().history, 1, "real output becomes accessible");
    let (_, achieved, history, _) = e.scroll_snapshot(100);
    assert_eq!(
        (achieved, history),
        (1, 1),
        "the padding prefix stays hidden"
    );
}

#[test]
fn history_keeps_blank_separators_and_styled_spaces() {
    let mut e = SessionEmu::new(20, 2);
    e.feed(b"old\r\n\r\nlive\r\nnext");
    assert_eq!(
        e.render().history,
        2,
        "blank separator follows real history"
    );
    let mut e = SessionEmu::new(20, 2);
    e.feed(b"\x1b[41m  \x1b[0m\r\nlive\r\nnext");
    assert_eq!(
        e.render().history,
        1,
        "colored space is not untouched padding"
    );
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
fn activity_keeps_real_braille_text_and_background_changes() {
    let mut emu = SessionEmu::new(80, 24);
    emu.feed(b"\x1b[48;2;30;30;30m ");
    let blank = emu.render();
    emu.feed("\x1b[H\x1b[38;2;20;20;20m⠁".as_bytes());
    let particle = emu.render();
    assert_eq!(blank.activity_hash, particle.activity_hash);
    assert_ne!(blank.content_hash, particle.content_hash);
    // A full render and a partial render must compute identical activity fingerprints.
    emu.render_cache.invalidate();
    assert_eq!(particle.activity_hash, emu.render().activity_hash);
    emu.feed("\x1b[H\x1b[38;2;240;240;240m⠁".as_bytes());
    let visible_braille = emu.render();
    assert_ne!(particle.activity_hash, visible_braille.activity_hash);
    emu.feed("\x1b[H\x1b[38;2;20;20;20m⠃".as_bytes());
    assert_ne!(particle.activity_hash, emu.render().activity_hash);
    emu.feed(b"\x1b[H\x1b[48;2;60;60;60m ");
    assert_ne!(blank.activity_hash, emu.render().activity_hash);
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
fn preserves_zero_width_emoji_components_with_cell_width() {
    let mut e = SessionEmu::new(20, 2);
    e.feed("👩‍💻x".as_bytes());
    let line = e.render().lines[0].to_string();
    assert_eq!(line, "\x1b[0m\x1b[3;2z👩‍💻\x1b[3Gx");

    let mut keycap = SessionEmu::new(20, 2);
    keycap.feed("1️⃣x".as_bytes());
    assert_eq!(
        keycap.render().lines[0].as_ref(),
        "\x1b[0m\x1b[3;2z1️⃣\x1b[3Gx"
    );

    let mut flag = SessionEmu::new(20, 2);
    flag.feed("🇺🇾x".as_bytes());
    assert_eq!(
        flag.render().lines[0].as_ref(),
        "\x1b[0m\x1b[2;2z🇺🇾\x1b[3Gx"
    );

    let mut modifier = SessionEmu::new(20, 2);
    modifier.feed("👍🏻x".as_bytes());
    assert_eq!(
        modifier.render().lines[0].as_ref(),
        "\x1b[0m\x1b[2;2z👍🏻\x1b[3Gx"
    );

    let mut gender = SessionEmu::new(20, 2);
    gender.feed("🧎‍♂️x".as_bytes());
    assert_eq!(
        gender.render().lines[0].as_ref(),
        "\x1b[0m\x1b[4;2z🧎‍♂️\x1b[3Gx"
    );

    let mut handshake = SessionEmu::new(30, 2);
    handshake.feed("🫱🏻‍🫲🏼 E14.0 handshake".as_bytes());
    assert_eq!(
        handshake.render().lines[0].as_ref(),
        "\x1b[0m\x1b[5;2z🫱🏻‍🫲🏼\x1b[3G E14.0 handshake"
    );

    let mut single_tone = SessionEmu::new(30, 2);
    single_tone.feed("🤝🏻 E14.0 handshake".as_bytes());
    assert_eq!(
        single_tone.render().lines[0].as_ref(),
        "\x1b[0m\x1b[2;2z🤝🏻\x1b[3G E14.0 handshake"
    );

    let mut qualified_heart = SessionEmu::new(30, 2);
    qualified_heart.feed("❤️‍🔥x".as_bytes());
    assert_eq!(
        qualified_heart.render().lines[0].as_ref(),
        "\x1b[0m\x1b[4;2z❤️‍🔥\x1b[3Gx"
    );

    let mut unqualified_heart = SessionEmu::new(30, 2);
    unqualified_heart.feed("❤‍🔥x".as_bytes());
    assert_eq!(
        unqualified_heart.render().lines[0].as_ref(),
        "\x1b[0m\x1b[3;1z❤‍🔥x"
    );

    let mut exclamation = SessionEmu::new(30, 2);
    exclamation.feed("❣️x".as_bytes());
    assert_eq!(
        exclamation.render().lines[0].as_ref(),
        "\x1b[0m\x1b[2;2z❣️\x1b[3Gx"
    );

    for emoji in ["🙂‍↔️", "🙂‍↔", "🙂‍↕️", "🙂‍↕", "👯🏻‍♀️", "👯🏻‍♀"]
    {
        let mut emu = SessionEmu::new(30, 2);
        emu.feed(format!("{emoji}x").as_bytes());
        let frame = emu.render();
        assert_eq!((frame.cx, frame.cy), (3, 0), "{emoji}");
        assert!(frame.lines[0].contains(emoji), "{emoji}");
        assert!(frame.lines[0].ends_with("\x1b[3Gx"), "{emoji}");
    }
    let mut non_emoji = SessionEmu::new(20, 2);
    non_emoji.feed("😀‍ax".as_bytes());
    let frame = non_emoji.render();
    assert_eq!((frame.cx, frame.cy), (4, 0));
}

#[test]
fn modifiers_are_zero_width_outside_emoji_sequences() {
    for modifier in '\u{1f3fb}'..='\u{1f3ff}' {
        for (prefix, columns) in [("", 0), (" ", 1), ("a", 1), ("好", 2)] {
            let mut emu = SessionEmu::new(10, 2);
            emu.feed(format!("{prefix}{modifier}x").as_bytes());
            let frame = emu.render();
            assert_eq!((frame.cx, frame.cy), (columns + 1, 0));
            if prefix.is_empty() {
                assert_eq!(frame.lines[0].as_ref(), "\x1b[0mx");
            } else {
                assert!(frame.lines[0].contains(&format!("{prefix}{modifier}")));
            }
        }
        let mut emu = SessionEmu::new(2, 2);
        emu.feed(format!("ab{modifier}x").as_bytes());
        let frame = emu.render();
        assert_eq!((frame.cx, frame.cy), (1, 1));
        assert!(frame.lines[0].contains(&format!("b{modifier}")));
        assert_eq!(frame.lines[1].as_ref(), "\x1b[0mx");
    }
}

#[test]
fn supplementary_narrow_glyph_does_not_reserve_a_spacer() {
    let mut e = SessionEmu::new(20, 2);
    e.feed("\u{1d400}x".as_bytes());
    assert_eq!(e.render().lines[0].as_ref(), "\x1b[0m\u{1d400}x");
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
    // Select a steady bar cursor with DECSCUSR 6.
    // Enable SGR mouse click reporting.
    e.feed(b"\x1b[6 q\x1b[?1006h\x1b[?1000h");
    let f = e.render();
    assert_eq!(f.cursor_shape, 2);
    assert!(f.app_mouse);
    assert!(!f.alt_screen);
    assert!(!f.cursor_blink);
    // Report clicks only. Keep drag events available for terminal selection.
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
    // OSC 2 also sets the title. An empty title value clears it.
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
    // Report the bell in the next frame only.
    // The session table keeps the notification until the user views the session.
    assert!(e.render().bell);
    assert!(!e.render().bell);
    // Report multiple bells between renders as one bell event.
    e.feed(b"\x07\x07");
    assert!(e.render().bell);
    // Preserve a pending bell when creating a scroll snapshot.
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
    // Taking a clipboard update removes it from the queue.
    assert!(e.take_clip().is_none());
    // Keep only the latest clipboard update.
    e.feed(b"\x1b]52;c;Zmlyc3Q=\x07\x1b]52;c;c2Vjb25k\x07");
    assert_eq!(e.take_clip().as_deref(), Some("second"));
    // Ignore primary-selection updates because the clipboard tools do not support them.
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
fn clear_viewport_erases_in_place_without_creating_history() {
    let mut e = SessionEmu::new(20, 3);
    e.feed(b"startup\r\nsecond\r\nthird\x1b[2;4H\x1b7");
    let before = e.render();
    // Split the CSI across control-mode packets, as happens during startup.
    e.feed(b"\x1b[2");
    e.feed(b"J");
    let cleared = e.render();
    assert_eq!(cleared.history, 0);
    assert_eq!((cleared.cx, cleared.cy), (3, 1));
    assert_ne!(before.content_hash, cleared.content_hash);
    assert!(cleared.lines.iter().all(|line| line.as_ref() == "\x1b[0m"));
    e.feed(b"\x1b[Hnew\x1b8!");
    let redraw = e.render();
    assert!(redraw.lines[0].contains("new"));
    assert!(redraw.lines[1].contains("!"));
    assert_eq!((redraw.cx, redraw.cy), (4, 1));
}

#[test]
fn clear_viewport_preserves_existing_history_and_background() {
    let mut e = SessionEmu::new(20, 2);
    e.feed(b"history\r\none\r\ntwo");
    assert_eq!(e.render().history, 1);
    e.feed(b"\x1b[41m\x1b[2J");
    assert_eq!(e.render().history, 1);
    assert_eq!(e.term.grid()[Line(0)][Column(0)].c, ' ');
    assert_eq!(
        e.term.grid()[Line(0)][Column(0)].bg,
        e.term.grid().cursor.template.bg
    );
    e.feed(b"\x1b[3J");
    assert_eq!(e.render().history, 0);
}

#[test]
fn synchronized_clear_waits_for_end_and_keeps_primary_history() {
    let mut e = SessionEmu::new(20, 2);
    e.feed(b"history\r\none\r\ntwo");
    let before = e.render();
    e.feed(b"\x1b[?2026h\x1b[2J\x1b[Hredraw");
    assert_eq!(e.render().content_hash, before.content_hash);
    e.feed(b"\x1b[?2026l");
    let after = e.render();
    assert_eq!(after.history, 1);
    assert!(after.lines[0].contains("redraw"));
    assert_eq!(after.lines[1].as_ref(), "\x1b[0m");

    e.feed(b"\x1b[?1049hfullscreen\x1b[2J");
    assert!(e
        .render()
        .lines
        .iter()
        .all(|line| line.as_ref() == "\x1b[0m"));
    e.feed(b"\x1b[?1049l");
    let primary = e.render();
    assert_eq!(primary.history, 1);
    assert!(primary.lines[0].contains("redraw"));
}

#[test]
fn erase_above_on_second_row_updates_cached_first_row() {
    let mut e = SessionEmu::new(20, 3);
    e.feed(b"first\r\nsecond\r\nthird");
    e.render();
    e.feed(b"\x1b[2;3H\x1b[1J");
    let frame = e.render();
    assert_eq!(frame.lines[0].as_ref(), "\x1b[0m");
    assert!(frame.lines[1].contains("ond"));
    assert!(frame.lines[2].contains("third"));
}

#[test]
fn seeding_a_full_viewport_does_not_create_spurious_history() {
    let mut e = SessionEmu::new(20, 4);
    e.feed(b"\x1b[H\x1b[0mline-0\r\nline-1\r\nline-2\r\nline-3\x1b[4;1H");

    assert_eq!(e.render().history, 0);
}

#[test]
fn resizing_a_full_viewport_preserves_displaced_text() {
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

    e.resize(20, rows - 1);
    assert_eq!(e.render().history, 1);
    assert_eq!(e.term.grid()[Line(-1)][Column(0)].c, 'l');
}

#[test]
fn repeated_resizes_do_not_turn_blank_padding_into_history() {
    let mut e = SessionEmu::new(20, 4);
    e.feed(b"\x1b[4;1Hprompt");
    e.resize(20, 3);
    assert_eq!(e.render().history, 0);
    e.resize(20, 2);
    let frame = e.render();
    assert_eq!(frame.history, 0);
    assert!(frame.lines[1].contains("prompt"));
}

#[test]
fn resizing_preserves_existing_blank_history() {
    let mut e = SessionEmu::new(20, 4);
    e.feed(b"\x1b[4;1H\r\nprompt");
    assert_eq!(e.history_lines(), 1);
    e.resize(20, 3);
    assert!(e.history_lines() >= 1);
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

    e.resize(20, rows - 1);
    assert!(e.render().history >= 1);
}
