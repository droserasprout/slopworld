use super::*;
use crate::application::AuthChange;
use crate::config::HostTerminalCfg;
use crate::session::test_manager;

#[tokio::test]
async fn settings_cannot_clear_root_authority_or_change_accepted_state_on_failure() {
    let manager = test_manager(Config::default());
    let before = std::fs::read_to_string(manager.cfg_path()).unwrap();
    let token = manager.config().await.daemon.token.clone();
    for blank in ["", "  ", "\t\n"] {
        let error = manager
            .configuration()
            .patch_config(serde_json::json!({"daemon": {"token": blank}}))
            .await
            .unwrap_err();
        assert!(error.to_string().contains("non-empty [daemon] token"));
        let text = toml::to_string_pretty(&crate::config::Settings {
            daemon: crate::config::Daemon {
                token: blank.into(),
                ..Default::default()
            },
            ..Default::default()
        })
        .unwrap();
        assert!(
            manager
                .configuration()
                .replace_config(&text)
                .await
                .unwrap_err()
                .to_string()
                .contains("non-empty [daemon] token")
        );
        assert_eq!(manager.config().await.daemon.token, token);
        assert_eq!(std::fs::read_to_string(manager.cfg_path()).unwrap(), before);
        assert_eq!(manager.config_authority.auth_generation(), 0);
        assert!(matches!(
            manager.config_authority.resolve_cap(Some(&token)).await,
            Some(crate::grant::Cap::Root)
        ));
    }
    let error = manager
        .configuration()
        .replace_config("[daemon]\nbind = '127.0.0.1:7717'\n")
        .await
        .unwrap_err();
    assert!(error.to_string().contains("non-empty [daemon] token"));
    manager
        .configuration()
        .patch_config(serde_json::json!({"daemon": {"token": crate::config::TOKEN_REDACTED}}))
        .await
        .unwrap();
    assert_eq!(manager.config().await.daemon.token, token);
}

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
    manager.config_authority.write().await.daemon.token = "root-for-revocation-test".into();
    let mut caps = Vec::new();
    for name in ["kept", "removed", "replaced"] {
        let token = manager
            .mint_grant(name.into(), vec![name.into()], crate::grant::Level::Rw)
            .await
            .unwrap();
        caps.push(
            manager
                .config_authority
                .resolve_cap(Some(&token))
                .await
                .unwrap(),
        );
    }
    manager
        .configuration()
        .update_cfg(ConfigMutation::Agents, |cfg| {
            cfg.sessions.retain(|session| session.name != "removed");
            cfg.sessions
                .iter_mut()
                .find(|session| session.name == "replaced")
                .unwrap()
                .state_id = crate::storage_id::draft_identity();
            Ok(())
        })
        .await
        .unwrap();
    assert!(caps[0].is_valid());
    assert!(!caps[1].is_valid());
    assert!(!caps[2].is_valid());
    assert_eq!(manager.config_authority.grant_count().await, 1);
}

#[tokio::test]
async fn persisted_root_token_changes_invalidate_existing_auth() {
    let manager = test_manager(Config::default());
    let mut changes = manager.config_authority.auth_changes();

    manager
        .configuration()
        .patch_config(serde_json::json!({"daemon": {"token": "rotated-root"}}))
        .await
        .expect("persist token rotation");

    assert_eq!(manager.config_authority.auth_generation(), 1);
    assert!(matches!(
        changes.try_recv().expect("auth invalidation"),
        AuthChange::RootTokenChanged
    ));
    assert_eq!(manager.config().await.daemon.token, "rotated-root");
    assert_eq!(
        Config::load(manager.cfg_path()).await.unwrap().daemon.token,
        "rotated-root"
    );
}

#[tokio::test]
async fn concurrent_structured_writes_keep_both_changes() {
    let manager = test_manager(Config::default());
    let left = manager.configuration();
    let right = manager.configuration();
    let (left, right) = tokio::join!(
        left.patch_config(serde_json::json!({"daemon": {"title_model": "left-model"}})),
        right.patch_config(serde_json::json!({"daemon": {"summary_prompt": "right-prompt"}})),
    );
    left.expect("left config write");
    right.expect("right config write");

    let cfg = manager.config().await;
    assert_eq!(cfg.daemon.title_model, "left-model");
    assert_eq!(cfg.daemon.summary_prompt, "right-prompt");
    drop(std::fs::remove_file(manager.cfg_path()));
}

#[tokio::test]
async fn raw_replacement_persists_the_real_token_for_a_redacted_candidate() {
    let mut cfg = Config::default();
    cfg.daemon.token = "real-root-token".into();
    let manager = test_manager(cfg.clone());
    let mut replacement = cfg;
    replacement.daemon.token = crate::config::TOKEN_REDACTED.into();

    manager
        .configuration()
        .replace_config(&toml::to_string_pretty(&replacement.settings).unwrap())
        .await
        .expect("replace config");

    assert_eq!(manager.config().await.daemon.token, "real-root-token");
    let persisted = std::fs::read_to_string(manager.cfg_path()).unwrap();
    assert!(persisted.contains("real-root-token"));
    assert!(!persisted.contains(crate::config::TOKEN_REDACTED));
    drop(std::fs::remove_file(manager.cfg_path()));
}

#[tokio::test]
async fn json_patch_preserves_omitted_fields_and_redacted_token() {
    let mut cfg = Config::default();
    cfg.daemon.token = "real-root-token".into();
    cfg.daemon.summary_prompt = "keep this prompt".into();
    let manager = test_manager(cfg.clone());

    manager
        .configuration()
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
    drop(std::fs::remove_file(manager.cfg_path()));
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
    assert!(!live["agent"].role.is_host());
    assert!(live["shell"].role.is_host());
    assert!(!live["shell"].role.is_durable_agent());
    assert_eq!(live["shell"].host_path, crate::config::expand("~/repo"));
    assert!(!live.contains_key("bad name"));
    assert_eq!(live.len(), 2);
}

#[tokio::test]
async fn invalid_library_does_not_consume_its_revision() {
    let manager = test_manager(Config::default());
    let accepted = manager
        .workspace
        .configuration
        .library_revision
        .lock()
        .unwrap()
        .clone();
    let dir = manager.cfg_path().parent().unwrap().join("prompts");
    std::fs::create_dir_all(&dir).unwrap();
    let path = dir.join("retry.toml");
    std::fs::write(&path, "name = [").unwrap();
    assert!(!manager.configuration().reload_if_changed().await);
    assert_eq!(
        *manager
            .workspace
            .configuration
            .library_revision
            .lock()
            .unwrap(),
        accepted
    );
    std::fs::write(&path, "name = 'retry'\nlink = 'project'\ntext = 'fixed'").unwrap();
    assert!(manager.configuration().reload_if_changed().await);
    assert_eq!(manager.config().await.library[0].text, "fixed");
}

#[tokio::test]
async fn configuration_entry_points_preserve_notification_scope() {
    let manager = crate::session::test_manager_with_socket(
        Config::default(),
        format!("slopworld-config-events-{}", uuid::Uuid::new_v4()),
    );
    let mut events = manager.application.subscribe();

    // Structured edits leave announcements to the operation that requested the edit.
    manager
        .configuration()
        .update_cfg(ConfigMutation::Agents, |_| Ok(()))
        .await
        .unwrap();
    assert!(events.try_recv().is_err());

    let text = toml::to_string_pretty(&manager.config().await.settings).unwrap();
    manager.configuration().replace_config(&text).await.unwrap();
    assert!(matches!(
        events.try_recv().unwrap().event(),
        Event::Projects { .. }
    ));
    assert!(matches!(
        events.try_recv().unwrap().event(),
        Event::Library { .. }
    ));
    assert!(events.try_recv().is_err());

    // External root edits are ignored; library reloads announce only the library.
    std::fs::write(manager.cfg_path(), "[daemon]\ntitle_model = 'external'").unwrap();
    assert!(!manager.configuration().reload_if_changed().await);
    assert!(events.try_recv().is_err());
    let dir = manager.cfg_path().parent().unwrap().join("prompts");
    std::fs::create_dir_all(&dir).unwrap();
    std::fs::write(dir.join("new.toml"), "name = 'new'\nlink = 'project'").unwrap();
    assert!(manager.configuration().reload_if_changed().await);
    assert!(matches!(
        events.try_recv().unwrap().event(),
        Event::Library { .. }
    ));
    assert!(events.try_recv().is_err());
}

#[tokio::test]
async fn document_save_does_not_acknowledge_an_external_library_revision() {
    let manager = test_manager(Config::default());
    let accepted = manager
        .workspace
        .configuration
        .library_revision
        .lock()
        .unwrap()
        .clone();
    let dir = manager.cfg_path().parent().unwrap().join("prompts");
    std::fs::create_dir_all(&dir).unwrap();
    std::fs::write(
        dir.join("external.toml"),
        "name = 'external'\nlink = 'project'",
    )
    .unwrap();
    manager
        .configuration()
        .patch_config(serde_json::json!({"daemon": {"title_model": "changed"}}))
        .await
        .unwrap();
    assert_eq!(
        *manager
            .workspace
            .configuration
            .library_revision
            .lock()
            .unwrap(),
        accepted
    );
    assert!(manager.configuration().reload_if_changed().await);
    assert_eq!(manager.config().await.library[0].name, "external");
}

#[tokio::test]
async fn agent_edit_does_not_read_or_retire_unselected_library_files() {
    let manager = test_manager(Config {
        sessions: vec![SessionCfg {
            name: "agent".into(),
            ..Default::default()
        }],
        ..Default::default()
    });
    let dir = manager.cfg_path().parent().unwrap().join("prompts");
    tokio::fs::create_dir_all(&dir).await.unwrap();
    let sibling = dir.join("external.toml");
    let bytes = "unaccepted external edit [";
    tokio::fs::write(&sibling, bytes).await.unwrap();
    let stamp = manager
        .workspace
        .configuration
        .library_revision
        .lock()
        .unwrap()
        .clone();
    manager
        .configuration()
        .update_cfg(ConfigMutation::Agents, |cfg| {
            cfg.sessions[0].label = Some("accepted".into());
            Ok(())
        })
        .await
        .unwrap();
    assert_eq!(tokio::fs::read_to_string(sibling).await.unwrap(), bytes);
    assert_eq!(
        *manager
            .workspace
            .configuration
            .library_revision
            .lock()
            .unwrap(),
        stamp
    );
    assert_eq!(
        manager.config().await.sessions[0].label.as_deref(),
        Some("accepted")
    );
}

#[tokio::test]
async fn library_edit_does_not_write_or_accept_an_external_root_revision() {
    let manager = test_manager(Config::default());
    let bytes = "invalid pending root edit [";
    tokio::fs::write(manager.cfg_path(), bytes).await.unwrap();
    manager
        .configuration()
        .update_cfg(ConfigMutation::Library, |cfg| {
            for name in ["new-prompt", "another-prompt"] {
                cfg.library.push(LibraryItemCfg {
                    name: name.into(),
                    ..Default::default()
                });
            }
            Ok(())
        })
        .await
        .unwrap();
    assert_eq!(
        tokio::fs::read_to_string(manager.cfg_path()).await.unwrap(),
        bytes
    );
    assert_eq!(manager.config().await.library[0].name, "new-prompt");
    assert_eq!(
        *manager
            .workspace
            .configuration
            .library_revision
            .lock()
            .unwrap(),
        crate::config::catalog::revision(manager.cfg_path()).ok()
    );
    assert_eq!(
        Config::load_library_for(manager.cfg_path()).await.unwrap()[0].name,
        "another-prompt"
    );
}

#[tokio::test]
async fn mutation_cannot_change_an_unowned_store_or_publish_its_candidate() {
    let manager = test_manager(Config::default());
    let before = tokio::fs::read(manager.cfg_path()).await.unwrap();
    let error = manager
        .configuration()
        .update_cfg(ConfigMutation::Agents, |cfg| {
            cfg.daemon.token = "not-owned".into();
            Ok(())
        })
        .await
        .unwrap_err();
    assert!(error.to_string().contains("does not own daemon"));
    assert_eq!(tokio::fs::read(manager.cfg_path()).await.unwrap(), before);
    assert_ne!(manager.config().await.daemon.token, "not-owned");
    let error = manager
        .configuration()
        .update_cfg(ConfigMutation::HostShells, |cfg| {
            cfg.library.push(LibraryItemCfg {
                name: "wrong-store".into(),
                ..Default::default()
            });
            Ok(())
        })
        .await
        .unwrap_err();
    assert!(error.to_string().contains("does not own library"));
    assert!(manager.config().await.library.is_empty());
}

#[tokio::test]
async fn failed_prepared_commit_keeps_accepted_snapshot_and_allows_retry() {
    let manager = test_manager(Config {
        sessions: vec![SessionCfg {
            name: "agent".into(),
            ..Default::default()
        }],
        ..Default::default()
    });
    let ((), prepared) = manager
        .configuration()
        .prepare_cfg_change(ConfigMutation::Agents, |cfg| {
            cfg.sessions[0].label = Some("new label".into());
            Ok(((), true))
        })
        .await
        .unwrap();
    assert_eq!(manager.config().await.sessions[0].label, None);
    let path = manager
        .cfg_path()
        .parent()
        .unwrap()
        .join("data/agents")
        .join(format!(
            "{}.toml",
            manager.config().await.sessions[0].state_id
        ));
    let original = tokio::fs::read(&path).await.unwrap();
    tokio::fs::remove_file(&path).await.unwrap();
    tokio::fs::create_dir(&path).await.unwrap();
    assert!(
        manager
            .configuration()
            .commit_prepared_cfg(prepared.unwrap())
            .await
            .is_err()
    );
    assert_eq!(manager.config().await.sessions[0].label, None);
    tokio::fs::remove_dir(&path).await.unwrap();
    tokio::fs::write(&path, original).await.unwrap();
    manager
        .configuration()
        .update_cfg(ConfigMutation::Agents, |cfg| {
            cfg.sessions[0].label = Some("retry".into());
            Ok(())
        })
        .await
        .unwrap();
    assert_eq!(
        manager.config().await.sessions[0].label.as_deref(),
        Some("retry")
    );
}

#[tokio::test]
async fn daemon_settings_reader_observes_only_accepted_settings_and_survives_manager_drop() {
    let manager = test_manager(Config::default());
    let reader = manager.config_authority.daemon_settings();
    let before = reader.snapshot().await.usage_poll_secs;
    let next = before + 60;
    let mut changes = manager.config_authority.auth_changes();
    let old_token = reader.snapshot().await.token;
    let patch = serde_json::json!({"daemon": {"usage_poll_secs": next, "token": "retry-root"}});
    let fault = crate::paths::fail_writes(manager.cfg_path());
    manager
        .configuration()
        .patch_config(patch.clone())
        .await
        .unwrap_err();
    assert_eq!(reader.snapshot().await.usage_poll_secs, before);
    assert_eq!(reader.snapshot().await.token, old_token);
    assert_eq!(manager.config_authority.root_generation(), 0);
    assert!(matches!(
        changes.try_recv(),
        Err(tokio::sync::broadcast::error::TryRecvError::Empty)
    ));
    assert!(matches!(
        manager.config_authority.resolve_cap(Some(&old_token)).await,
        Some(crate::grant::Cap::Root)
    ));
    drop(fault);
    manager.configuration().patch_config(patch).await.unwrap();
    assert_eq!(reader.snapshot().await.usage_poll_secs, next);
    assert_eq!(manager.config_authority.root_generation(), 1);
    assert!(matches!(
        changes.try_recv().unwrap(),
        AuthChange::RootTokenChanged
    ));
    assert!(
        manager
            .config_authority
            .resolve_cap(Some(&old_token))
            .await
            .is_none()
    );
    let weak = Arc::downgrade(&manager);
    drop(manager);
    assert!(weak.upgrade().is_none());
    assert_eq!(reader.snapshot().await.usage_poll_secs, next);
}

#[tokio::test]
async fn configuration_updates_only_its_workspace_endpoint_after_acceptance() {
    let manager = test_manager(Config::default());
    let other = test_manager(Config::default());
    let endpoint = manager.cfg_path().with_extension("endpoint.toml");
    let other_endpoint = other.cfg_path().with_extension("endpoint.toml");
    let original = toml::to_string(&crate::endpoint::Endpoint {
        url: "http://127.0.0.1:7717".into(),
        token: manager.config().await.daemon.token.clone(),
    })
    .unwrap();
    std::fs::write(&endpoint, &original).unwrap();
    std::fs::write(&other_endpoint, &original).unwrap();

    for token in ["rotated-once", "rotated-twice"] {
        manager
            .configuration()
            .patch_config(serde_json::json!({
                "daemon": { "token": token, "bind": "127.0.0.1:8818" }
            }))
            .await
            .unwrap();
        let saved: crate::endpoint::Endpoint =
            toml::from_str(&std::fs::read_to_string(&endpoint).unwrap()).unwrap();
        assert_eq!(saved.token, token);
        assert_eq!(saved.url, "http://127.0.0.1:7717");
        assert_eq!(manager.config().await.daemon.token, token);
        assert_eq!(std::fs::read_to_string(&other_endpoint).unwrap(), original);
    }
    let accepted = std::fs::read(&endpoint).unwrap();
    manager
        .configuration()
        .patch_config(serde_json::json!({"daemon": {"token": ""}}))
        .await
        .unwrap_err();
    assert_eq!(std::fs::read(&endpoint).unwrap(), accepted);
}
