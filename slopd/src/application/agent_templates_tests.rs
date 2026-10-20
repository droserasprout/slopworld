//! Catalog mutations run without a session manager.
use super::*;

fn template(name: &str, description: &str) -> AgentTemplate {
    serde_json::from_value(serde_json::json!({
        "name": name, "description": description, "defaults": {}
    }))
    .unwrap()
}

#[tokio::test]
async fn template_writes_compare_versions_and_preserve_rejected_drafts() {
    let fixture = super::super::test_support::Fixture::new();
    let catalog = &fixture.services.templates;
    catalog.create(template("reviewer", "first")).await.unwrap();
    let original = catalog.list().await.remove(0);
    let mut edited = original.clone();
    edited.description = "winner".into();
    let winner = catalog
        .replace("reviewer", original.version, edited)
        .await
        .unwrap();
    assert!(winner.version > original.version);

    // Two clients can submit changes to the same revision. Accept exactly one update.
    let mut left = winner.clone();
    left.description = "winner".into();
    let right = left.clone();
    let (left, right) = tokio::join!(
        catalog.replace("reviewer", winner.version, left),
        catalog.replace("reviewer", winner.version, right),
    );
    assert_ne!(left.is_ok(), right.is_ok());
    let winner = left.or(right).unwrap();

    let mut stale_draft = original.clone();
    stale_draft.description = "rejected draft".into();
    let error = catalog
        .replace("reviewer", original.version, stale_draft)
        .await
        .unwrap_err();
    assert!(
        error
            .downcast_ref::<crate::session::AgentTemplateError>()
            .is_some()
    );
    assert_eq!(catalog.list().await[0].description, "winner");

    let stale_delete = catalog.remove("reviewer", original.version).await;
    assert!(stale_delete.is_err());
    catalog.remove("reviewer", winner.version).await.unwrap();
    let missing_edit = catalog
        .replace("reviewer", winner.version, winner.clone())
        .await;
    assert!(matches!(
        missing_edit
            .unwrap_err()
            .downcast_ref::<crate::session::AgentTemplateError>(),
        Some(crate::session::AgentTemplateError::Missing(_))
    ));
    let mut recreated = winner;
    recreated.name = "reviewer".into();
    recreated.version = 0;
    let recreated = catalog.create(recreated).await.unwrap();
    assert!(recreated.version > original.version);
}

#[tokio::test]
async fn failed_save_preserves_catalog_and_version_then_retry_survives_reload() {
    let fixture = super::super::test_support::Fixture::new();
    let catalog = &fixture.services.templates;
    let original = catalog
        .create(template("reviewer", "original"))
        .await
        .unwrap();
    let mut edited = original.clone();
    edited.description = "accepted on retry".into();
    // Block the committed index update after the candidate has been prepared.
    let fault = crate::paths::fail_writes(&catalog.path.join(".index.toml"));
    let error = catalog
        .replace("reviewer", original.version, edited.clone())
        .await
        .unwrap_err();
    assert!(error.downcast_ref::<TemplatePersistence>().is_some());
    assert_eq!(catalog.list().await[0].description, "original");
    assert_eq!(catalog.list().await[0].version, original.version);
    drop(fault);
    let saved = catalog
        .replace("reviewer", original.version, edited)
        .await
        .unwrap();
    let reloaded = AgentTemplates::load(catalog.path.clone()).await.unwrap();
    assert_eq!(reloaded.list().await[0].description, "accepted on retry");
    assert_eq!(reloaded.list().await[0].version, saved.version);
}

#[tokio::test]
async fn caller_cancellation_cannot_separate_catalog_commit_from_publication() {
    let fixture = super::super::test_support::Fixture::new();
    let catalog = &fixture.services.templates;
    let template: AgentTemplate = serde_json::from_value(serde_json::json!({
        "name": "owned", "description": "Committed despite cancellation", "defaults": {}
    }))
    .unwrap();
    let reached = Arc::new(tokio::sync::Notify::new());
    let release = Arc::new(tokio::sync::Notify::new());
    *catalog.publish_pause.lock().unwrap() = Some((reached.clone(), release.clone()));
    let caller = {
        let services = fixture.services.clone();
        tokio::spawn(async move { services.templates.create(template).await })
    };
    reached.notified().await;
    assert!(catalog.list().await.is_empty());
    assert!(
        AgentTemplateStore::load(&catalog.path)
            .await
            .unwrap()
            .get("owned")
            .is_some()
    );
    caller.abort();
    assert!(caller.await.unwrap_err().is_cancelled());
    release.notify_one();
    // The mutation gate is held through publication by the detached owner.
    let _done = catalog.mutation.lock().await;
    assert_eq!(
        catalog.list().await[0].description,
        "Committed despite cancellation"
    );
}
