use super::{strip_sgr, strip_sgr_lines, strip_sgr_tail};

#[test]
fn linewise_stripping_preserves_joined_screen_text() {
    let lines = vec![
        "\x1b[31".to_string(),
        "mred\x1b[0m".to_string(),
        "blank".to_string(),
    ];
    assert_eq!(strip_sgr_lines(&lines), strip_sgr(&lines.join("\n")));
}

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
fn tail_stripping_omits_trailing_blank_rows() {
    let lines = vec!["old", "prompt", "", ""];
    assert_eq!(strip_sgr_tail(&lines, 3), "old\nprompt");
}

#[test]
fn tail_stripping_preserves_blank_rows_inside_the_tail() {
    let lines = vec!["old", "", "prompt", "", "next"];
    assert_eq!(strip_sgr_tail(&lines, 4), "\nprompt\n\nnext");
}

#[test]
fn tail_stripping_drops_stale_prompts_above_the_tail() {
    let lines = vec!["stale prompt", "one", "two", "three", "current"];
    assert_eq!(strip_sgr_tail(&lines, 3), "two\nthree\ncurrent");
}

#[test]
fn tail_stripping_returns_empty_for_a_blank_screen() {
    let lines = vec!["", "\x1b[31m", "   "];
    assert!(strip_sgr_tail(&lines, 12).is_empty());
}
