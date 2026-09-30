use super::strip_sgr;

#[test]
fn stripping_preserves_ascii_controls_unicode_and_incomplete_escapes() {
    assert_eq!(
        strip_sgr("\x1b[32mASCII\t日本語 🦀\x1b[0m\r\n"),
        "ASCII\t日本語 🦀\r\n"
    );
    assert_eq!(strip_sgr("text\x1b"), "text\x1b");
    assert_eq!(strip_sgr("text\x1b[31"), "text");
    assert_eq!(strip_sgr("\x1b]0;title\x07prompt"), "prompt");
    let ascii: String = (0u8..=127)
        .filter(|byte| *byte != 0x1b)
        .map(char::from)
        .collect();
    assert_eq!(strip_sgr(&ascii), ascii);
}

#[test]
fn osc_string_terminator_consumes_both_bytes() {
    assert_eq!(strip_sgr("before\x1b]0;title\x1b\\after"), "beforeafter");
    assert_eq!(strip_sgr("\x1b]title\x1bXhidden\x1b\\visible"), "visible");
}
