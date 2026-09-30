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
    // Parse a line that ends inside a UTF-8 sequence.
    // Pass the raw bytes to the VT parser so it can reconstruct the sequence.
    assert_eq!(parse_output(b"%output %0 A\xf0\x9f").unwrap(), b"A\xf0\x9f");
}

#[test]
fn octal_decoding_retains_byte_boundaries_and_wrapping() {
    assert_eq!(unescape(br"\000\377\400\777"), [0, 255, 0, 255]);
}
