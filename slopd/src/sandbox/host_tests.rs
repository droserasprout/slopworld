use super::*;

#[test]
fn splits_quoted_args() {
    assert_eq!(shell_split("claude").unwrap(), vec!["claude"]);
    assert_eq!(
        shell_split(r#"claude --model opus "two words""#).unwrap(),
        vec!["claude", "--model", "opus", "two words"]
    );
    assert_eq!(shell_split("a  b").unwrap(), vec!["a", "b"]);
    assert_eq!(shell_split(r#"x ''"#).unwrap(), vec!["x", ""]);
    assert_eq!(shell_split(r#"'a'\''b'"#).unwrap(), vec!["a'b"]);
    assert_eq!(shell_split(r#"x\ y"#).unwrap(), vec!["x y"]);
    assert_eq!(
        shell_split(r#"bash -lc 'cd -- '\''a b'\'' && exec "${SHELL:-bash}"'"#).unwrap(),
        vec!["bash", "-lc", "cd -- 'a b' && exec \"${SHELL:-bash}\""]
    );
}

#[test]
fn double_quoted_backslashes_preserve_ordinary_characters_and_escape_shell_specials() {
    assert_eq!(
        shell_split(r#"agent "a\qb" "a\\b" "a\"b" "a\$b" "a\`b""#).unwrap(),
        ["agent", r"a\qb", r"a\b", "a\"b", "a$b", "a`b"]
    );
    assert_eq!(shell_split("agent \"a\\\nb\"").unwrap(), ["agent", "ab"]);
}

#[test]
fn rejects_malformed_commands_without_exposing_their_contents() {
    for command in ["agent 'secret", "agent \"secret", "agent secret\\"] {
        let error = shell_split(command).unwrap_err().to_string();
        assert!(error.contains("invalid command"));
        assert!(!error.contains("secret"));
    }
}

#[test]
fn shell_words_preserve_literals_and_recognize_comments() {
    assert_eq!(
        shell_split("agent '$HOME' '*.rs' café\u{a0}word name#tag # ignored").unwrap(),
        ["agent", "$HOME", "*.rs", "café\u{a0}word", "name#tag"]
    );
    assert_eq!(shell_split("# comment only").unwrap(), Vec::<String>::new());
}

#[test]
fn host_commands_reject_invalid_or_missing_executables() {
    let cfg = Config::default();
    let project = ProjectCfg::default();
    for command in ["agent 'unfinished", "# comment only", "'' argument"] {
        let session = SessionCfg {
            cmd: Some(command.into()),
            ..Default::default()
        };
        host_argv(&cfg, &session, &project).unwrap_err();
    }
}
