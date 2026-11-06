//! Policy edits authorize existing identities immediately; old token scopes cannot bypass them.
use crate::config::{AgentPermissions, Config, SessionCfg};
use crate::credentials::{AgentCredential, Cap, Level};
use crate::session::test_manager;

#[tokio::test]
async fn current_policy_selects_direct_workers_or_project_agents_and_denies_hosts() {
    let mut cfg = Config::default();
    let parent = SessionCfg {
        name: "parent".into(),
        project: "repo".into(),
        ..Default::default()
    };
    let child = SessionCfg {
        name: "child".into(),
        project: "repo".into(),
        worker: true,
        parent_id: parent.state_id.clone(),
        ..Default::default()
    };
    let grandchild = SessionCfg {
        name: "grandchild".into(),
        project: "repo".into(),
        worker: true,
        parent_id: child.state_id.clone(),
        ..Default::default()
    };
    cfg.sessions = vec![
        parent,
        child,
        grandchild,
        SessionCfg {
            name: "peer".into(),
            project: "repo".into(),
            ..Default::default()
        },
        SessionCfg {
            name: "outside".into(),
            project: "elsewhere".into(),
            ..Default::default()
        },
    ];
    let manager = test_manager(cfg);
    let cap = Cap::Scoped(AgentCredential {
        grantor: "parent".into(),
        sessions: ["outside".into()].into(),
        level: Level::Rw,
        revoked: Default::default(),
    });
    for (scope, allowed) in [
        ("deny", vec![]),
        ("own_workers", vec!["child"]),
        ("project_agents", vec!["child", "grandchild", "peer"]),
        ("deny", vec![]),
    ] {
        let permissions: AgentPermissions = serde_json::from_value(serde_json::json!({
            "followup": scope, "cancel": scope, "terminal": scope, "lifecycle": scope, "delete": scope,
            "worktrees": scope != "deny"
        })).unwrap();
        manager.config_authority.write().await.sessions[0].permissions = permissions;
        for name in ["child", "grandchild", "peer", "outside"] {
            let expected = allowed.contains(&name);
            let actions = manager.worker_access(&cap, name).await;
            assert_eq!(actions.is_some_and(|access| access.followup), expected);
            assert_eq!(actions.is_some_and(|access| access.cancel), expected);
            assert_eq!(
                manager.terminal_cap_ok(&cap, name, Level::Rw).await,
                expected,
                "terminal {scope} {name}"
            );
            assert_eq!(
                manager.worker_lifecycle_ok(&cap, name).await,
                expected,
                "lifecycle {scope} {name}"
            );
            assert_eq!(
                manager.delete_agent_ok(&cap, name).await,
                expected,
                "delete {scope} {name}"
            );
        }
        assert_eq!(manager.manage_worktrees_ok(&cap).await, scope != "deny");
        assert!(!manager.delete_agent_ok(&cap, "parent").await);
        assert!(manager.worker_diagnostics_ok(&cap, "child").await);
    }
}

#[tokio::test]
async fn replacement_parent_identity_does_not_inherit_own_worker_permissions() {
    let parent = SessionCfg {
        name: "parent".into(),
        ..Default::default()
    };
    let child = SessionCfg {
        name: "child".into(),
        worker: true,
        parent_id: parent.state_id.clone(),
        ..Default::default()
    };
    let manager = test_manager(Config {
        sessions: vec![parent, child],
        ..Default::default()
    });
    let cap = Cap::Scoped(AgentCredential {
        grantor: "parent".into(),
        sessions: Default::default(),
        level: Level::Rw,
        revoked: Default::default(),
    });
    assert!(manager.worker_access(&cap, "child").await.unwrap().followup);
    manager.config_authority.write().await.sessions[0].state_id = "replacement".into();
    assert!(manager.worker_access(&cap, "child").await.is_none());
}
