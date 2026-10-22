use super::*;

#[test]
fn explicit_bat_resolves_the_host_alias_without_rewriting_arguments() {
    let debian = |name: &str| name == "batcat";
    for (command, expected) in [
        ("bat", "batcat"),
        (
            "  bat\t--theme='bat' -- %s",
            "  batcat\t--theme='bat' -- %s",
        ),
        ("bat --paging=always", "batcat --paging=always"),
        ("batcat --paging=always", "batcat --paging=always"),
        ("/opt/bat --paging=always", "/opt/bat --paging=always"),
        ("wrapper bat", "wrapper bat"),
        ("battery", "battery"),
    ] {
        assert_eq!(resolve_pager_with(command, debian), expected);
        assert_eq!(resolve_highlighter_with(command, debian), expected);
        assert_eq!(resolve_pager_with(command, |_| true), command);
        assert_eq!(resolve_highlighter_with(command, |_| false), command);
    }
}

#[test]
fn automatic_readers_select_batcat_and_keep_its_child_pager() {
    for (installed, pager) in [
        (
            vec!["batcat", "less"],
            "batcat --paging=always --pager 'less -RS --shift=1 --wheel-lines=1'",
        ),
        (
            vec!["batcat", "more"],
            "batcat --paging=always --pager more",
        ),
        (vec!["batcat"], "batcat --paging=never"),
    ] {
        let available = |name: &str| installed.contains(&name);
        assert_eq!(resolve_pager_with("auto", available), pager);
        assert_eq!(
            resolve_highlighter_with("auto", available),
            "batcat --color=always --style=plain --paging=never"
        );
    }
}

#[test]
fn automatic_readers_prefer_bat_and_preserve_fallbacks() {
    let both = |name: &str| ["bat", "batcat"].contains(&name);
    assert_eq!(resolve_pager_with("auto", both), "bat --paging=never");
    assert_eq!(
        resolve_highlighter_with("auto", both),
        "bat --color=always --style=plain --paging=never"
    );
    assert_eq!(resolve_pager_with("auto", |_| false), "more");
    assert_eq!(resolve_pager_with("auto", |name| name == "less"), "less");
    assert_eq!(resolve_highlighter_with("auto", |_| false), "");
    for command in ["", "wrapper batcat", "bat --paging=never"] {
        assert_eq!(resolve_pager_with(command, both), command);
        assert_eq!(resolve_highlighter_with(command, both), command);
    }
}
