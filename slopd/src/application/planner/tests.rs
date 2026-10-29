use super::*;
use std::sync::Arc;

fn fixture() -> (Application, super::super::test_support::Fixture) {
    let mut fixture = super::super::test_support::Fixture::new();
    Arc::get_mut(&mut fixture.services).unwrap().planner =
        Store::new(fixture.root.join("TODO.txt"));
    let mut app = crate::application::test_application(crate::config::Config::default());
    app.services = fixture.services.clone();
    (app, fixture)
}
fn action(revision: String) -> Action {
    Action {
        action: "execute".into(),
        revision,
        line: 0,
        project: "missing".into(),
        template: "missing".into(),
    }
}

#[tokio::test]
async fn planner_dispatch_rejects_stale_credentials_revision_and_policy_before_reserving() {
    let (app, _fixture) = fixture();
    let original = app.services.planner.read().await.unwrap();
    let document = app
        .services
        .planner
        .save("Task".into(), original.revision.clone())
        .await
        .unwrap();
    let generation = app.config_authority.root_generation();
    assert!(
        dispatch(
            &app,
            action(document.revision.clone()),
            generation + 1,
            "Work".into()
        )
        .await
        .err()
        .unwrap()
        .to_string()
        .contains("credentials")
    );
    assert!(
        dispatch(&app, action(original.revision), generation, "Work".into())
            .await
            .err()
            .unwrap()
            .to_string()
            .contains("changed")
    );
    assert!(
        dispatch(&app, action(document.revision), generation, "Work".into())
            .await
            .err()
            .unwrap()
            .to_string()
            .contains("project")
    );
    assert_eq!(
        app.services.planner.read().await.unwrap().text,
        document.text
    );
}

#[tokio::test]
async fn planner_dispatch_reservations_survive_restart_and_prevent_repeated_launches() {
    let (app, fixture) = fixture();
    let empty = app.services.planner.read().await.unwrap();
    let document = app
        .services
        .planner
        .save("Task sw-task:pending".into(), empty.revision)
        .await
        .unwrap();
    let restored = Store::new(fixture.root.join("TODO.txt"))
        .read()
        .await
        .unwrap();
    assert_eq!(restored.text, document.text);
    let result = dispatch(
        &app,
        action(restored.revision),
        app.config_authority.root_generation(),
        "Work".into(),
    )
    .await;
    assert!(
        result
            .err()
            .unwrap()
            .to_string()
            .contains("already been dispatched")
    );
}
