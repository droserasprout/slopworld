use super::*;
use crate::emu::SessionEmu;

#[test]
fn maps_color_to_index_sgr() {
    let mut e = SessionEmu::new(20, 2);
    e.feed(b"\x1b[1;31mX\x1b[0m");
    let f = e.render();
    assert_eq!(f.lines[0].as_ref(), "\x1b[0m\x1b[0;1;31mX");
}

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
    // This CJK character occupies two cells. Position the next run at column 3 with CHA.
    e.feed("\u{4f60}X".as_bytes());
    let f = e.render();
    assert_eq!(f.lines[0].as_ref(), "\x1b[0m\u{4f60}\x1b[3GX");
}

#[test]
fn long_combining_cluster_preserves_text_and_geometry() {
    let cluster = format!("e{}", "\u{301}".repeat(32));
    let mut emu = SessionEmu::new(10, 2);
    emu.feed(format!("{cluster}x").as_bytes());
    let frame = emu.render();
    assert_eq!((frame.cx, frame.cy), (2, 0));
    assert_eq!(
        frame.lines[0].as_ref(),
        format!("\x1b[0m\x1b[33;1z{cluster}x")
    );
}

#[test]
fn trailing_wide_glyph_preserves_occupied_end() {
    for glyph in ["好", "\u{20000}", "\u{1f600}"] {
        let mut e = SessionEmu::new(2, 2);
        e.feed(format!("\x1b[41m{glyph}").as_bytes());
        let f = e.render();
        assert_eq!(
            f.lines[0].as_ref(),
            format!("\x1b[0m\x1b[0;41m{glyph}\x1b[3G")
        );
    }
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
    let mut e = SessionEmu::new(6, 2);
    e.feed(b"\x1b]8;;https://a.example\x1b\\linked");
    let f = e.render();
    assert_eq!(
        f.lines[0],
        "\x1b[0m\x1b]8;;https://a.example\x1b\\linked\x1b]8;;\x1b\\".into()
    );
}

#[test]
fn strips_escapes_out_of_a_uri() {
    assert_eq!(safe_uri("https://a\u{1b}b\u{7}c"), "https://abc");
    assert_eq!(safe_uri(&"x".repeat(4000)).len(), 2048);
}
