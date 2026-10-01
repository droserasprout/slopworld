use super::*;

async fn fixture() -> Arc<Manager> {
    let mut cfg = Config {
        sessions: ["removed", "kept"]
            .into_iter()
            .map(|name| SessionCfg {
                name: name.into(),
                autostart: false,
                ..Default::default()
            })
            .collect(),
        ..Default::default()
    };
    cfg.daemon.token = "root".into();
    let manager = crate::session::test_manager(cfg);
    manager.update_cfg(|_| Ok(())).await.unwrap();
    manager
}

#[tokio::test]
async fn narrow_removal_preserves_document_and_does_not_touch_other_catalogs() {
    let manager = fixture().await;
    let text = format!(
        "# keep this comment\n{}\n[future]\npolicy = 'keep'\n",
        tokio::fs::read_to_string(&manager.cfg_path).await.unwrap()
    );
    Config::save_text(&manager.cfg_path, &text).await.unwrap();
    manager.mark_saved_document(&text).await;
    // A deletion must not load/reconcile unrelated worktrees or parse a newly
    // edited library. Maintenance still owns accepting those external changes.
    tokio::fs::write(
        manager.cfg_path.with_file_name("worktrees.toml"),
        "invalid TOML [",
    )
    .await
    .unwrap();
    let library = manager.cfg_path.parent().unwrap().join("prompts");
    tokio::fs::create_dir_all(&library).await.unwrap();
    let library_file = library.join("external.toml");
    tokio::fs::write(&library_file, "pending external edit [")
        .await
        .unwrap();
    let library_stamp = *manager.config_state.library_mtime.lock().unwrap();
    let token = manager
        .mint_grant(
            "removed".into(),
            vec!["removed".into()],
            crate::grant::Level::Rw,
        )
        .await
        .unwrap();
    let cap = manager.resolve_cap(Some(&token)).await.unwrap();
    manager.remove_configured_session("removed").await.unwrap();
    assert!(!cap.is_valid());
    assert!(manager.config().await.session("removed").is_none());
    assert!(manager.config().await.session("kept").is_some());
    let saved = tokio::fs::read_to_string(&manager.cfg_path).await.unwrap();
    assert!(saved.starts_with("# keep this comment"));
    assert!(saved.contains("policy = 'keep'"));
    assert!(Config::parse(&saved).unwrap().session("removed").is_none());
    assert_eq!(
        tokio::fs::read_to_string(&library_file).await.unwrap(),
        "pending external edit ["
    );
    assert_eq!(
        *manager.config_state.library_mtime.lock().unwrap(),
        library_stamp
    );
}

#[tokio::test]
async fn narrow_removal_failure_keeps_memory_and_allows_retry() {
    let manager = fixture().await;
    let original = tokio::fs::read_to_string(&manager.cfg_path).await.unwrap();
    let fault = crate::paths::fail_writes(&manager.cfg_path);
    manager
        .remove_configured_session("removed")
        .await
        .unwrap_err();
    assert!(manager.config().await.session("removed").is_some());
    assert_eq!(
        tokio::fs::read_to_string(&manager.cfg_path).await.unwrap(),
        original
    );
    drop(fault);
    manager.remove_configured_session("removed").await.unwrap();
    manager.remove_configured_session("kept").await.unwrap();
    assert!(manager.config().await.sessions.is_empty());
    assert!(
        Config::load(&manager.cfg_path)
            .await
            .unwrap()
            .sessions
            .is_empty()
    );
}

#[tokio::test]
async fn narrow_removal_declines_unaccepted_disk_revisions() {
    let manager = fixture().await;
    let original = tokio::fs::read_to_string(&manager.cfg_path).await.unwrap();
    // Explicit stale stamp makes the ordering deterministic without sleeps.
    *manager.config_state.cfg_mtime.lock().unwrap() = Some(std::time::UNIX_EPOCH);
    assert!(
        Config::session_removal_text(&manager.cfg_path, "removed", Some(std::time::UNIX_EPOCH))
            .await
            .unwrap()
            .is_none()
    );
    assert_eq!(
        tokio::fs::read_to_string(&manager.cfg_path).await.unwrap(),
        original
    );
    // The regular path still supports fixtures and alternate/unaccepted documents.
    manager.remove_configured_session("removed").await.unwrap();
    assert!(
        Config::load(&manager.cfg_path)
            .await
            .unwrap()
            .session("removed")
            .is_none()
    );
}
