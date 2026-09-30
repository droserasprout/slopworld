use super::*;

#[test]
fn usage_sources_use_rows_without_provider_switches() {
    let mut daemon = crate::config::Daemon::default();
    assert!(provider_enabled(&daemon, "anthropic"));
    assert!(!provider_enabled(&daemon, "openrouter"));
    assert!(provider_enabled(&daemon, "openai"));

    daemon.usage_items.insert(
        OPENROUTER_BALANCE.into(),
        crate::config::UsageItem {
            poll: true,
            interval_secs: None,
        },
    );
    daemon.usage_items.insert(
        CLAUDE_SESSION.into(),
        crate::config::UsageItem {
            poll: false,
            interval_secs: None,
        },
    );
    assert!(provider_enabled(&daemon, "openrouter"));
    assert!(!provider_enabled(&daemon, "anthropic"));
    assert!(!item_enabled(&daemon, "anthropic", "claude_session"));
}

#[test]
fn resolved_rows_keep_disabled_and_missing_states_explicit() {
    let mut daemon = crate::config::Daemon::default();
    daemon.usage_items.insert(
        CLAUDE_SESSION.into(),
        crate::config::UsageItem {
            poll: false,
            interval_secs: None,
        },
    );

    let rows = resolve_rows(&Snapshot::default(), &daemon);
    let session = rows.iter().find(|row| row.key == CLAUDE_SESSION).unwrap();
    let balance = rows
        .iter()
        .find(|row| row.key == OPENROUTER_BALANCE)
        .unwrap();
    assert!(!session.poll);
    assert!(session.window.is_none());
    assert!(!balance.poll);
    assert!(balance.window.is_none());
}

#[test]
fn a_partial_provider_table_disables_implicit_rows() {
    let mut daemon = crate::config::Daemon::default();
    daemon.usage_items.insert(
        CLAUDE_SESSION.into(),
        crate::config::UsageItem {
            poll: false,
            interval_secs: None,
        },
    );

    let rows = resolve_rows(&Snapshot::default(), &daemon);
    for key in [CLAUDE_SESSION, CLAUDE_WEEK, CLAUDE_SPEND] {
        let row = rows.iter().find(|row| row.key == key).unwrap();
        assert!(!row.poll, "disabled provider leaves no pollable {key} row");
    }
}
