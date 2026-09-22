use super::*;

#[tokio::test]
async fn errands_require_host_or_settings_before_allocating_and_copy_template_once() {
    let cfg = Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: "/tmp".into(),
            ..Default::default()
        }],
        ..Default::default()
    };
    let manager = crate::session::test_manager(cfg.clone());
    let mut item = LibraryItemCfg {
        name: "review".into(),
        project: "repo".into(),
        ..Default::default()
    };
    let want = RunWhere::default();
    let error = manager
        .create_errand_session(&cfg, &item, &want, false, false, "")
        .await
        .unwrap_err();
    assert!(error.to_string().contains("Host or an agent template"));
    assert!(manager.live.read().await.is_empty());
    assert!(manager.temp.read().await.is_empty());
    let template: AgentTemplate = serde_json::from_value(serde_json::json!({
            "name": "offline", "defaults": {"cmd":"review-agent", "network":"none", "limits":{"memory_mb":256},
                "autostart":true,"auto_resume":true,"persistent_tmp":true,
                "sandbox":["captured"], "sandbox_presets":[{"name":"captured","ro":["/usr"]}]}
        })).unwrap();
    manager.templates.write().await.create(template).unwrap();
    item.agent_template = "offline".into();
    let name = manager
        .create_errand_session(&cfg, &item, &want, false, false, "")
        .await
        .unwrap();
    {
        let live = manager.live.read().await;
        let session = &live[&name].cfg;
        assert_eq!(session.network, crate::config::NetworkMode::None);
        assert_eq!(session.limits.memory_mb, Some(256));
        assert_eq!(session.cmd.as_deref(), Some("review-agent"));
        assert_eq!(session.sandbox_snapshots[0].ro, ["/usr"]);
        assert!(session.persistent_tmp);
        assert!(!session.autostart && !session.auto_resume);
    }
    manager.templates.write().await.templates.clear();
    assert_eq!(
        manager.live.read().await[&name].cfg.limits.memory_mb,
        Some(256)
    );
    assert!(manager
        .create_errand_session(&cfg, &item, &want, false, false, "")
        .await
        .is_err());
    item.agent_template.clear();
    item.name = "host-command".into();
    let host = manager
        .create_errand_session(&cfg, &item, &want, true, false, "")
        .await
        .unwrap();
    assert!(manager.live.read().await[&host].host);
}
