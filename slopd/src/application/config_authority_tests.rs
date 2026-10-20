//! Publication contracts without a session runtime or persistence adapter.
use super::*;
use crate::config::transactions::ConfigChange;

fn config(token: &str) -> Config {
    let mut config = Config::default();
    config.daemon.token = token.into();
    config
}

#[tokio::test]
async fn settings_lock_excludes_rotation_until_credentials_and_epoch_can_publish_together() {
    let config_authority = ConfigAuthority::new(config("old"), crate::grant::Grants::default());
    let settings = config_authority.daemon_settings();
    let mut changes = config_authority.auth_changes();
    let read = config_authority.read_config().await;
    let publication = config_authority.publish_config(ConfigChange {
        new: config("new"),
        root_token_changed: true,
    });
    tokio::pin!(publication);
    // Poll the writer while the reader is held; no sleep or scheduler race is needed.
    assert!(futures::poll!(&mut publication).is_pending());
    assert_eq!(config_authority.root_generation(), 0);
    assert!(matches!(
        changes.try_recv(),
        Err(tokio::sync::broadcast::error::TryRecvError::Empty)
    ));
    assert_eq!(read.daemon.token, "old");
    drop(read);
    assert_eq!(publication.await, "new");
    assert_eq!(settings.snapshot().await.token, "new");
    assert_eq!(config_authority.root_generation(), 1);
    assert_eq!(config_authority.auth_generation(), 1);
    assert!(matches!(
        changes.try_recv().unwrap(),
        AuthChange::RootTokenChanged
    ));
    assert!(config_authority.resolve_cap(Some("old")).await.is_none());
    assert!(matches!(
        config_authority.resolve_cap(Some("new")).await,
        Some(crate::grant::Cap::Root)
    ));
}

#[tokio::test]
async fn unchanged_credentials_preserve_epochs_and_settings_reader_outlives_owner() {
    let config_authority = ConfigAuthority::new(config("root"), crate::grant::Grants::default());
    let settings = config_authority.daemon_settings();
    let mut changes = config_authority.auth_changes();
    let mut next = config("root");
    next.daemon.usage_poll_secs += 60;
    let expected = next.daemon.usage_poll_secs;
    config_authority
        .publish_config(ConfigChange {
            new: next,
            root_token_changed: false,
        })
        .await;
    assert_eq!(config_authority.root_generation(), 0);
    assert_eq!(config_authority.auth_generation(), 0);
    assert!(matches!(
        changes.try_recv(),
        Err(tokio::sync::broadcast::error::TryRecvError::Empty)
    ));
    drop(config_authority);
    assert_eq!(settings.snapshot().await.usage_poll_secs, expected);
}
