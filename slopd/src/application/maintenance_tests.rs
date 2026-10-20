use super::*;

#[tokio::test]
async fn catalogs_still_wake_maintenance_without_live_sessions() {
    let sessions = crate::session::test_manager(Default::default());
    let services = &sessions.application;
    assert!(sessions.maintenance_delay().await > Duration::from_secs(60));
    assert_eq!(services.maintenance_delay(&sessions).await, Duration::ZERO);
    let now = unix_ms();
    for catalog in [Catalog::Config, Catalog::Presets, Catalog::Jukebox] {
        assert!(services.catalogs.claim(catalog, now));
    }
    assert!(services.maintenance_delay(&sessions).await <= Duration::from_secs(2));
}
