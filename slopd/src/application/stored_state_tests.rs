use super::*;

#[tokio::test]
async fn measured_inventory_purges_expired_trash_while_quick_inventory_retains_it() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let session = crate::config::SessionCfg {
        name: "agent".into(),
        state_id: "0123456789abcdef".into(),
        ..Default::default()
    };
    let cfg = Config {
        sessions: vec![session.clone()],
        ..Default::default()
    };
    let private = crate::sandbox::state_dir(&session).unwrap();
    std::fs::create_dir_all(&private).unwrap();
    std::fs::write(private.join("memory"), "remember me").unwrap();
    let expired = crate::sandbox::trash_state(&session, "reset")
        .unwrap()
        .unwrap();
    std::fs::File::open(&expired)
        .unwrap()
        .set_modified(
            std::time::SystemTime::now() - std::time::Duration::from_secs(15 * 24 * 60 * 60),
        )
        .unwrap();
    std::fs::create_dir_all(&private).unwrap();
    std::fs::write(private.join("memory"), "fresh state").unwrap();
    let recent = crate::sandbox::trash_state(&session, "recent")
        .unwrap()
        .unwrap();
    std::fs::create_dir_all(&private).unwrap();
    std::fs::write(private.join("memory"), "current state").unwrap();

    let quick = stored_states(cfg.clone(), false).await.unwrap();
    assert_eq!(
        quick.iter().filter(|entry| entry.kind == "trash").count(),
        2
    );
    assert!(quick.iter().all(|entry| entry.bytes == 0));
    assert!(expired.exists());

    // Repeated measured scans clean up only expired trash and retain usable inventory.
    for _ in 0..2 {
        let measured = stored_states(cfg.clone(), true).await.unwrap();
        assert_eq!(
            measured
                .iter()
                .filter(|entry| entry.kind == "trash")
                .count(),
            1
        );
        assert!(
            measured
                .iter()
                .any(|entry| entry.kind == "active" && entry.bytes > 0)
        );
        assert!(!expired.exists());
        assert!(recent.exists() && private.exists());
    }
}

#[tokio::test]
async fn administration_protects_configured_state_and_repeated_cleanup_preserves_it() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let session = crate::config::SessionCfg {
        name: "agent".into(),
        state_id: "0123456789abcdef".into(),
        ..Default::default()
    };
    let cfg = Config {
        sessions: vec![session.clone()],
        ..Default::default()
    };
    let private = crate::sandbox::state_dir(&session).unwrap();
    std::fs::create_dir_all(&private).unwrap();
    std::fs::write(private.join("memory"), "retained").unwrap();
    assert!(
        delete_stored_state(cfg.clone(), "orphan", &session.state_id)
            .await
            .is_err()
    );
    assert!(
        delete_stored_state(cfg.clone(), "trash", "../escape")
            .await
            .is_err()
    );
    let archived = crate::sandbox::trash_state(&session, "reset")
        .unwrap()
        .unwrap();
    std::fs::create_dir_all(&private).unwrap();
    std::fs::write(private.join("memory"), "current").unwrap();
    for _ in 0..2 {
        empty_trash().await.unwrap();
        assert!(!archived.exists());
        assert_eq!(
            std::fs::read_to_string(private.join("memory")).unwrap(),
            "current"
        );
    }
    delete_stored_state(Config::default(), "orphan", &session.state_id)
        .await
        .unwrap();
    assert!(!private.exists());
}
