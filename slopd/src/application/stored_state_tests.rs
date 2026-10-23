use super::*;
use std::future::Future;

#[tokio::test]
async fn inventory_retains_expired_trash_for_both_size_modes() {
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

    // Inventory remains read-only, including repeated measured scans.
    for _ in 0..2 {
        let measured = stored_states(cfg.clone(), true).await.unwrap();
        assert_eq!(
            measured
                .iter()
                .filter(|entry| entry.kind == "trash")
                .count(),
            2
        );
        assert!(
            measured
                .iter()
                .any(|entry| entry.kind == "active" && entry.bytes > 0)
        );
        assert!(expired.exists());
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
    let application = crate::application::test_application(cfg.clone());
    let private = crate::sandbox::state_dir(&session).unwrap();
    std::fs::create_dir_all(&private).unwrap();
    std::fs::write(private.join("memory"), "retained").unwrap();
    assert!(
        delete_stored_state(&application, "orphan", &session.state_id)
            .await
            .is_err()
    );
    assert!(
        delete_stored_state(&application, "trash", "../escape")
            .await
            .is_err()
    );
    let archived = crate::sandbox::trash_state(&session, "reset")
        .unwrap()
        .unwrap();
    std::fs::create_dir_all(&private).unwrap();
    std::fs::write(private.join("memory"), "current").unwrap();
    for _ in 0..2 {
        empty_trash(&application).await.unwrap();
        assert!(!archived.exists());
        assert_eq!(
            std::fs::read_to_string(private.join("memory")).unwrap(),
            "current"
        );
    }
    let unowned = crate::application::test_application(Config::default());
    delete_stored_state(&unowned, "orphan", &session.state_id)
        .await
        .unwrap();
    assert!(!private.exists());
}

// Isolated test processes keep this one-shot blocking-worker gate local to a test.
type CleanupPause = (
    std::sync::Arc<tokio::sync::Notify>,
    std::sync::mpsc::Receiver<()>,
);
static CLEANUP_PAUSE: std::sync::Mutex<Option<CleanupPause>> = std::sync::Mutex::new(None);

pub(super) fn pause_cleanup() {
    let pause = CLEANUP_PAUSE.lock().unwrap().take();
    if let Some((reached, release)) = pause {
        reached.notify_one();
        release
            .recv_timeout(std::time::Duration::from_secs(10))
            .unwrap();
    }
}

#[tokio::test]
async fn inventory_during_trash_metadata_publication_preserves_old_state() {
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
    let application = crate::application::test_application(cfg.clone());
    let private = crate::sandbox::state_dir(&session).unwrap();
    std::fs::create_dir_all(&private).unwrap();
    std::fs::write(private.join("memory"), "recover me").unwrap();
    std::fs::File::open(&private)
        .unwrap()
        .set_modified(
            std::time::SystemTime::now() - std::time::Duration::from_secs(15 * 24 * 60 * 60),
        )
        .unwrap();

    application
        .sessions
        .session_operation(async {
            // Hold the exact filesystem stage between trash_state's rename and metadata
            // write. A root inventory GET bypasses this boundary and must be harmless.
            let trash = private.parent().unwrap().join(".trash");
            std::fs::create_dir_all(&trash).unwrap();
            let archived = trash.join("pending-reset");
            std::fs::rename(&private, &archived).unwrap();
            for sizes in [false, true, true] {
                stored_states(cfg.clone(), sizes).await.unwrap();
                assert_eq!(
                    std::fs::read_to_string(archived.join("memory")).unwrap(),
                    "recover me"
                );
            }
            std::fs::write(
                archived.join(".slopworld-session.toml"),
                toml::to_string(&session).unwrap(),
            )
            .unwrap();
            crate::sandbox::restore_stored_state("pending-reset", &cfg.sessions).unwrap();
            crate::sandbox::finish_restored_state(&session).unwrap();
            assert_eq!(
                std::fs::read_to_string(private.join("memory")).unwrap(),
                "recover me"
            );
        })
        .await;
}

async fn delete_archive(owner: &Application, empty_all: bool, key: &str) -> Result<()> {
    if empty_all {
        empty_trash(owner).await
    } else {
        delete_stored_state(owner, "trash", key).await
    }
}

#[tokio::test]
async fn canceled_blocking_cleanup_excludes_restore_until_deletion_finishes() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let session = crate::config::SessionCfg {
        name: "agent".into(),
        state_id: "0123456789abcdef".into(),
        ..Default::default()
    };
    let application = crate::application::test_application(Config {
        sessions: vec![session.clone()],
        ..Default::default()
    });
    let private = crate::sandbox::state_dir(&session).unwrap();
    for empty_all in [false, true] {
        std::fs::create_dir_all(&private).unwrap();
        std::fs::write(private.join("memory"), "archive").unwrap();
        let archived = crate::sandbox::trash_state(&session, "reset")
            .unwrap()
            .unwrap();
        let key = archived.file_name().unwrap().to_str().unwrap().to_owned();
        let reached = std::sync::Arc::new(tokio::sync::Notify::new());
        let (release, receiver) = std::sync::mpsc::channel();
        *CLEANUP_PAUSE.lock().unwrap() = Some((reached.clone(), receiver));
        let owner = application.clone();
        let delete_key = key.clone();
        let request = tokio::spawn(async move {
            // Root mutation middleware owns this outer scope. Cleanup must inherit
            // its guard and keep it after this request is aborted.
            owner
                .sessions
                .session_operation(async { delete_archive(&owner, empty_all, &delete_key).await })
                .await
        });
        tokio::time::timeout(std::time::Duration::from_secs(5), reached.notified())
            .await
            .unwrap();
        request.abort();
        assert!(request.await.unwrap_err().is_cancelled());
        application
            .sessions
            .session_boundary_fixture()
            .try_write()
            .unwrap_err();
        assert!(archived.join("memory").exists());

        let restore = crate::application::SessionEdits::new(&application);
        let restore = restore.restore_stored_state(&key);
        tokio::pin!(restore);
        assert!(
            std::future::poll_fn(|cx| std::task::Poll::Ready(restore.as_mut().poll(cx)))
                .await
                .is_pending()
        );
        release.send(()).unwrap();
        let result = tokio::time::timeout(std::time::Duration::from_secs(5), restore)
            .await
            .unwrap();
        assert!(result.is_err(), "restore must see the completed deletion");
        assert!(!archived.exists());
        assert!(
            !private.exists(),
            "cleanup must not race a rename into active state"
        );
        drop(
            application
                .sessions
                .session_boundary_fixture()
                .try_write()
                .unwrap(),
        );
    }
}

#[tokio::test]
async fn cleanup_waits_for_restore_and_preserves_restored_contents() {
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
    let application = crate::application::test_application(cfg.clone());
    let private = crate::sandbox::state_dir(&session).unwrap();
    for empty_all in [false, true] {
        std::fs::create_dir_all(&private).unwrap();
        std::fs::write(private.join("memory"), "restored").unwrap();
        let archived = crate::sandbox::trash_state(&session, "reset")
            .unwrap()
            .unwrap();
        let key = archived.file_name().unwrap().to_str().unwrap().to_owned();
        let (request, expected_success) = application
            .sessions
            .session_operation(async {
                let owner = application.clone();
                let delete_key = key.clone();
                let (started, admitted) = tokio::sync::oneshot::channel();
                let request = tokio::spawn(async move {
                    let operation = delete_archive(&owner, empty_all, &delete_key);
                    tokio::pin!(operation);
                    // Poll admission while the restore boundary is held, then signal
                    // only once cleanup is demonstrably waiting on that boundary.
                    std::future::poll_fn(|cx| {
                        assert!(operation.as_mut().poll(cx).is_pending());
                        std::task::Poll::Ready(())
                    })
                    .await;
                    started.send(()).unwrap();
                    operation.await
                });
                admitted.await.unwrap();
                assert!(archived.join("memory").exists());
                crate::sandbox::restore_stored_state(&key, &cfg.sessions).unwrap();
                crate::sandbox::finish_restored_state(&session).unwrap();
                (request, empty_all)
            })
            .await;
        let result = tokio::time::timeout(std::time::Duration::from_secs(5), request)
            .await
            .unwrap()
            .unwrap();
        assert_eq!(result.is_ok(), expected_success);
        assert_eq!(
            std::fs::read_to_string(private.join("memory")).unwrap(),
            "restored"
        );
        std::fs::remove_dir_all(&private).unwrap();
    }
}
