use crate::config::{Config, HostTerminalCfg, SessionCfg};
use crate::session::test_manager;

#[tokio::test]
async fn config_sync_upserts_agents_and_only_valid_host_terminals() {
    let manager = test_manager(Config::default());
    let cfg = Config {
        sessions: vec![SessionCfg {
            name: "agent".into(),
            ..Default::default()
        }],
        host_terminals: vec![
            HostTerminalCfg {
                name: "bad name".into(),
                ..Default::default()
            },
            HostTerminalCfg {
                name: "agent".into(),
                ..Default::default()
            },
            HostTerminalCfg {
                name: "shell".into(),
                label: None,
                project: "repo".into(),
                path: "~/repo".into(),
                autostart: true,
                ..Default::default()
            },
        ],
        ..Default::default()
    };
    manager.upsert_sessions(&cfg).await;
    manager.upsert_host_terminals(&cfg).await;

    let live = manager.live.read().await;
    assert!(!live["agent"].role.is_host());
    assert!(live["shell"].role.is_host());
    assert!(!live["shell"].role.is_durable_agent());
    assert_eq!(live["shell"].host_path, crate::config::expand("~/repo"));
    assert!(!live.contains_key("bad name"));
    assert_eq!(live.len(), 2);
}
