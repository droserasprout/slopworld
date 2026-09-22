use super::*;
use crate::config::{HostTerminalCfg, StateRule};
use crate::session::test_manager;
use std::time::{Duration, UNIX_EPOCH};

#[test]
fn new_live_starts_as_a_boot_placeholder() {
    let cfg = SessionCfg {
        name: "agent".into(),
        project: "project".into(),
        ..Default::default()
    };
    let live = Live::new(cfg.clone(), TitleCapture::default());

    assert_eq!(live.cfg.name, cfg.name);
    assert_eq!(live.cfg.project, cfg.project);
    assert!(!live.ephemeral);
    assert!(!live.host);
    assert!(live.host_path.is_empty());
    assert_eq!(live.state, State::Down);
    assert_eq!((live.cols, live.rows), (BOOT_COLS, BOOT_ROWS));
    assert_eq!(live.seq, 0);
    assert_eq!(live.state_since, 0);
    assert!(!live.bell);
    assert!(live.screen.is_none());
    assert!(live.emu.is_none());
    assert!(live.reader.is_none());
    assert!(live.input.is_none());
    assert!(live.breadcrumbs.is_empty());
    assert!(!live.breadcrumbs_pending);
}

#[tokio::test]
async fn client_and_watch_guards_release_their_bookkeeping() {
    let manager = test_manager(Config::default());
    assert!(!manager.watched("agent"));

    let first_client = manager.client_joined();
    let second_client = manager.client_joined();
    assert_eq!(manager.signals.clients.load(Ordering::Relaxed), 2);
    assert!(manager.signals.clients_since.load(Ordering::Relaxed) > 0);
    drop(second_client);
    assert_eq!(manager.signals.clients.load(Ordering::Relaxed), 1);
    drop(first_client);
    assert_eq!(manager.signals.clients.load(Ordering::Relaxed), 0);

    let first_watch = manager.watching("agent");
    let second_watch = manager.watching("agent");
    assert!(manager.watched("agent"));
    drop(second_watch);
    assert!(manager.watched("agent"));
    drop(first_watch);
    assert!(!manager.watched("agent"));
}

#[tokio::test]
async fn usage_broadcasts_only_when_the_readout_changes() {
    let manager = test_manager(Config::default());
    let mut events = manager.events.subscribe();
    let unchanged = crate::usage::Snapshot::default();
    manager.set_usage(unchanged).await;
    assert!(events.try_recv().is_err());

    let changed = crate::usage::Snapshot {
        ok: true,
        sources: vec!["test".into()],
        ..Default::default()
    };
    manager.set_usage(changed.clone()).await;
    let event = events.try_recv().expect("usage event");
    assert!(matches!(event.event(), Event::Usage { usage } if usage == &changed));
    assert_eq!(manager.usage().await, changed);
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
}

#[test]
fn candidate_preparation_restores_a_redacted_root_token() {
    let mut old = Config::default();
    old.daemon.token = "real-root-token".into();
    old.state_rules.push(StateRule {
        state: "idle".into(),
        pattern: "idle".into(),
    });
    let mut candidate = old.clone();
    candidate.daemon.token = crate::config::TOKEN_REDACTED.into();

    let change = prepare_candidate(&old, candidate).expect("valid candidate");

    assert_eq!(change.new.daemon.token, old.daemon.token);
    assert!(!change.rules.is_empty());
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
    let _ = std::fs::remove_file(manager.cfg_path.clone());
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
    let _ = std::fs::remove_file(manager.cfg_path.clone());
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
    let _ = std::fs::remove_file(manager.cfg_path.clone());
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

    let _ = std::fs::remove_file(path);
}
