use super::*;

#[test]
fn sandbox_arguments_escape_controls_without_merging_argument_boundaries() {
    assert_eq!(shell_quote("simple/path"), "simple/path");
    assert_eq!(shell_quote(""), "''");
    assert_eq!(shell_quote("two words"), "'two words'");
    assert_eq!(shell_quote("a'b"), "'a'\\''b'");
    let raw = "mount\nENV=value\r\t\x1b[31m\u{85}";
    let displayed = shell_quote(raw);
    assert_eq!(displayed, "'mount\\nENV=value\\r\\t\\u{1b}[31m\\u{85}'");
    assert!(!displayed.chars().any(char::is_control));
    // Display escaping never mutates the values offered to JSON output.
    let value = json!({"plan": {"mounts": [raw]}});
    assert_eq!(value["plan"]["mounts"][0], raw);
}
