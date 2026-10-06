use super::*;
use crate::config::HostTerminalCfg;
use crate::session::test_manager;
use std::time::{Duration, UNIX_EPOCH};

#[tokio::test]
async fn removing_and_replacing_sessions_revokes_only_changed_identities() {
    let manager = test_manager(Config {
        sessions: ["kept", "removed", "replaced"]
            .into_iter()
            .map(|name| SessionCfg {
                name: name.into(),
                ..Default::default()
            })
            .collect(),
        ..Default::default()
    });
    manager.cfg.write().await.daemon.token = "root-for-revocation-test".into();
    let mut caps = Vec::new();
    for name in ["kept", "removed", "replaced"] {
        let token = manager
            .mint_grant(name.into(), vec![name.into()], crate::grant::Level::Rw)
            .await
            .unwrap();
        caps.push(manager.resolve_cap(Some(&token)).await.unwrap());
    }
    manager
        .update_cfg(|cfg| {
            cfg.sessions.retain(|session| session.name != "removed");
            cfg.sessions
                .iter_mut()
                .find(|session| session.name == "replaced")
                .unwrap()
                .state_id = uuid::Uuid::new_v4().to_string();
            cfg.sessions.reverse();
            Ok(())
        })
        .await
        .unwrap();
    assert!(caps[0].is_valid());
    assert!(!caps[1].is_valid());
    assert!(!caps[2].is_valid());
    assert_eq!(manager.grant_count().await, 1);
}

#[tokio::test]
async fn persisted_root_token_changes_invalidate_existing_auth() {
    let manager = test_manager(Config::default());
    let mut changes = manager.auth_changes();

    manager
        .update_cfg(|cfg| {
            cfg.daemon.token = "rotated-root".into();
            Ok(())
        })
        .await
        .expect("persist token rotation");

    assert_eq!(manager.auth_generation(), 1);
    assert!(matches!(
        changes.try_recv().expect("auth invalidation"),
        AuthChange::RootTokenChanged
    ));
    assert_eq!(manager.config().await.daemon.token, "rotated-root");
    assert_eq!(
        Config::load(&manager.cfg_path).await.unwrap().daemon.token,
        "rotated-root"
    );
}

#[test]
fn candidate_preparation_restores_a_redacted_root_token() {
    let mut old = Config::default();
    old.daemon.token = "real-root-token".into();
    let mut candidate = old.clone();
    candidate.daemon.token = crate::config::TOKEN_REDACTED.into();

    let change = prepare_candidate(&old, candidate).expect("valid candidate");

    assert_eq!(change.new.daemon.token, old.daemon.token);
}

#[tokio::test]
async fn concurrent_structured_writes_keep_both_changes() {
    let manager = test_manager(Config::default());
    let left = manager.clone();
    let right = manager.clone();
    let (left, right) = tokio::join!(
        left.update_cfg(|cfg| {
            cfg.daemon.title_model = "left-model".into();
            Ok(())
        }),
        right.update_cfg(|cfg| {
            cfg.daemon.summary_prompt = "right-prompt".into();
            Ok(())
        }),
    );
    left.expect("left config write");
    right.expect("right config write");

    let cfg = manager.config().await;
    assert_eq!(cfg.daemon.title_model, "left-model");
    assert_eq!(cfg.daemon.summary_prompt, "right-prompt");
    drop(std::fs::remove_file(manager.cfg_path.clone()));
}

#[tokio::test]
async fn raw_replacement_persists_the_real_token_for_a_redacted_candidate() {
    let mut cfg = Config::default();
    cfg.daemon.token = "real-root-token".into();
    let manager = test_manager(cfg.clone());
    let mut replacement = cfg;
    replacement.daemon.token = crate::config::TOKEN_REDACTED.into();

    manager
        .replace_config(&toml::to_string_pretty(&replacement).unwrap())
        .await
        .expect("replace config");

    assert_eq!(manager.config().await.daemon.token, "real-root-token");
    let persisted = std::fs::read_to_string(&manager.cfg_path).unwrap();
    assert!(persisted.contains("real-root-token"));
    assert!(!persisted.contains(crate::config::TOKEN_REDACTED));
    drop(std::fs::remove_file(manager.cfg_path.clone()));
}

#[tokio::test]
async fn json_patch_preserves_omitted_fields_and_redacted_token() {
    let mut cfg = Config::default();
    cfg.daemon.token = "real-root-token".into();
    cfg.daemon.summary_prompt = "keep this prompt".into();
    let manager = test_manager(cfg.clone());
    cfg.save(&manager.cfg_path).await.unwrap();
    *manager.config_state.cfg_mtime.lock().unwrap() = tokio::fs::metadata(&manager.cfg_path)
        .await
        .unwrap()
        .modified()
        .ok();

    manager
        .patch_config(serde_json::json!({
            "daemon": {
                "token": crate::config::TOKEN_REDACTED,
                "title_min_chars": 42,
            }
        }))
        .await
        .expect("patch config");

    let updated = manager.config().await;
    assert_eq!(updated.daemon.token, "real-root-token");
    assert_eq!(updated.daemon.summary_prompt, "keep this prompt");
    assert_eq!(updated.daemon.title_min_chars, 42);
    drop(std::fs::remove_file(manager.cfg_path.clone()));
}

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
    assert!(!live["agent"].host);
    assert!(live["shell"].host);
    assert!(live["shell"].ephemeral);
    assert_eq!(live["shell"].host_path, crate::config::expand("~/repo"));
    assert!(!live.contains_key("bad name"));
    assert_eq!(live.len(), 2);
}

#[tokio::test]
async fn invalid_config_does_not_consume_its_disk_stamp() {
    let manager = test_manager(Config::default());
    let path = manager.cfg_path.clone();
    let old_stamp = UNIX_EPOCH;
    let failed_stamp = UNIX_EPOCH + Duration::from_secs(1);
    *manager.config_state.cfg_mtime.lock().unwrap() = Some(old_stamp);

    std::fs::write(&path, "[daemon\n").unwrap();
    std::fs::File::open(&path)
        .unwrap()
        .set_modified(failed_stamp)
        .unwrap();
    assert!(!manager.reload_if_changed().await);
    assert_eq!(
        *manager.config_state.cfg_mtime.lock().unwrap(),
        Some(old_stamp)
    );

    std::fs::write(&path, toml::to_string_pretty(&Config::default()).unwrap()).unwrap();
    std::fs::File::open(&path)
        .unwrap()
        .set_modified(failed_stamp)
        .unwrap();
    assert!(manager.reload_if_changed().await);
    assert_eq!(
        *manager.config_state.cfg_mtime.lock().unwrap(),
        Some(failed_stamp)
    );

    drop(std::fs::remove_file(path));
}

#[tokio::test]
async fn configuration_entry_points_preserve_notification_scope() {
    let manager = crate::session::test_manager_with_socket(
        Config::default(),
        format!("slopworld-config-events-{}", uuid::Uuid::new_v4()),
    );
    let mut events = manager.events.subscribe();

    // Structured edits leave announcements to the operation that requested the edit.
    manager
        .update_cfg(|cfg| {
            cfg.daemon.title_model = "new model".into();
            Ok(())
        })
        .await
        .unwrap();
    assert!(events.try_recv().is_err());

    let text = toml::to_string_pretty(&manager.config().await).unwrap();
    manager.replace_config(&text).await.unwrap();
    assert!(matches!(
        events.try_recv().unwrap().event(),
        Event::Projects { .. }
    ));
    assert!(matches!(
        events.try_recv().unwrap().event(),
        Event::Library { .. }
    ));
    assert!(events.try_recv().is_err());

    // An external reload also announces the authoritative session list.
    std::fs::File::open(&manager.cfg_path)
        .unwrap()
        .set_modified(UNIX_EPOCH + Duration::from_secs(1))
        .unwrap();
    assert!(manager.reload_if_changed().await);
    assert!(matches!(
        events.try_recv().unwrap().event(),
        Event::Sessions { .. }
    ));
    assert!(matches!(
        events.try_recv().unwrap().event(),
        Event::Projects { .. }
    ));
    assert!(matches!(
        events.try_recv().unwrap().event(),
        Event::Library { .. }
    ));
    assert!(events.try_recv().is_err());
}

#[tokio::test]
async fn save_acknowledgement_leaves_an_external_replacement_unseen() {
    let manager = test_manager(Config::default());
    let expected = toml::to_string(&manager.config().await).unwrap();
    let mut external = manager.config().await;
    external.daemon.title_model = "external model".into();
    external.save(&manager.cfg_path).await.unwrap();
    manager.mark_saved_document(&expected).await;
    assert!(manager.config_state.cfg_mtime.lock().unwrap().is_none());
    assert!(manager.reload_if_changed().await);
    assert_eq!(manager.config().await.daemon.title_model, "external model");
}

#[tokio::test]
async fn document_save_does_not_acknowledge_an_external_library_revision() {
    let manager = test_manager(Config::default());
    let expected = toml::to_string(&manager.config().await).unwrap();
    Config::save_text(&manager.cfg_path, &expected)
        .await
        .unwrap();
    let accepted = Some(UNIX_EPOCH);
    *manager.config_state.library_mtime.lock().unwrap() = accepted;
    manager.mark_saved_document(&expected).await;
    assert_eq!(
        *manager.config_state.library_mtime.lock().unwrap(),
        accepted
    );
}
