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

#[tokio::test]
async fn maintenance_reloads_library_and_accepts_shutdown() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let app = crate::application::test_application(Default::default());
    let mut events = app.services.subscribe();
    let prompts = app
        .workspace
        .settings_path()
        .parent()
        .unwrap()
        .join("prompts");
    std::fs::create_dir_all(&prompts).unwrap();
    std::fs::write(prompts.join("new.toml"), "name = 'new'\nlink = 'project'").unwrap();
    let poller = app
        .services
        .spawn_maintenance(app.sessions.clone(), Configuration::new(&app));
    tokio::time::timeout(Duration::from_secs(5), async {
        loop {
            let event = events.recv().await.unwrap();
            if matches!(event.event(), Event::Library { .. }) {
                break;
            }
        }
    })
    .await
    .unwrap();
    assert!(
        app.config_authority
            .snapshot()
            .await
            .library_items_all()
            .iter()
            .any(|item| item.name == "new")
    );
    poller.abort();
    assert!(poller.await.unwrap_err().is_cancelled());
}
