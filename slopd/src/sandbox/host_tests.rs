use super::*;

#[test]
fn splits_quoted_args() {
    assert_eq!(shell_split("claude"), vec!["claude"]);
    assert_eq!(
        shell_split(r#"claude --model opus "two words""#),
        vec!["claude", "--model", "opus", "two words"]
    );
    assert_eq!(shell_split("a  b"), vec!["a", "b"]);
    assert_eq!(shell_split(r#"x ''"#), vec!["x", ""]);
    assert_eq!(shell_split(r#"'a'\''b'"#), vec!["a'b"]);
    assert_eq!(shell_split(r#"x\ y"#), vec!["x y"]);
    assert_eq!(
        shell_split(r#"bash -lc 'cd -- '\''a b'\'' && exec "${SHELL:-bash}"'"#),
        vec!["bash", "-lc", "cd -- 'a b' && exec \"${SHELL:-bash}\""]
    );
}

#[test]
fn double_quoted_backslashes_preserve_ordinary_characters_and_escape_shell_specials() {
    assert_eq!(
        shell_split(r#"agent "a\qb" "a\\b" "a\"b" "a\$b" "a\`b""#),
        ["agent", r"a\qb", r"a\b", "a\"b", "a$b", "a`b"]
    );
    assert_eq!(shell_split("agent \"a\\\nb\""), ["agent", "ab"]);
}
