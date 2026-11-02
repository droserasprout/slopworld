use super::*;
use crate::application::test_support::Fixture;

async fn setup() -> (Fixture, Store, String) {
    let fixture = Fixture::new();
    let store = Store::new(fixture.root.join("TODO.txt"));
    let empty = store.read().await.unwrap();
    let saved = store
        .save("Task +project".into(), empty.revision)
        .await
        .unwrap();
    let id = saved.items[0].id.clone();
    (fixture, store, id)
}

#[tokio::test]
async fn description_survives_task_edits_completion_deletion_and_restoration() {
    let (fixture, store, id) = setup().await;
    let empty = store.read_description(&id).await.unwrap();
    assert!(empty.text.is_empty());
    let markdown = "# Details\n\n- [ ] Verify λ\n\n```rust\nfn main() {}\n```\n";
    let description = store
        .save_description(&id, markdown.into(), empty.revision)
        .await
        .unwrap();
    let original = store.read().await.unwrap();
    assert!(!original.text.contains("Details"));
    let completed = store
        .save(
            format!("x 2026-11-02 Renamed sw-id:{id}"),
            original.revision,
        )
        .await
        .unwrap();
    assert_eq!(
        store.read_description(&id).await.unwrap().revision,
        description.revision
    );
    let deleted = store.save(String::new(), completed.revision).await.unwrap();
    store.read_description(&id).await.unwrap_err();
    store
        .save_description(&id, "stale".into(), description.revision)
        .await
        .unwrap_err();
    let path = fixture.root.join(format!("planner/descriptions/{id}.md"));
    assert_eq!(tokio::fs::read_to_string(path).await.unwrap(), markdown);
    store.save(original.text, deleted.revision).await.unwrap();
    assert_eq!(store.read_description(&id).await.unwrap().text, markdown);
}

#[tokio::test]
async fn stale_and_external_description_edits_are_rejected_independently_of_backlog() {
    let (fixture, store, id) = setup().await;
    let empty = store.read_description(&id).await.unwrap();
    let saved = store
        .save_description(&id, "First".into(), empty.revision.clone())
        .await
        .unwrap();
    store
        .save_description(&id, "Stale".into(), empty.revision)
        .await
        .unwrap_err();
    let path = fixture.root.join(format!("planner/descriptions/{id}.md"));
    tokio::fs::write(&path, "External").await.unwrap();
    store
        .save_description(&id, "Overwrite".into(), saved.revision)
        .await
        .unwrap_err();
    assert_eq!(store.read_description(&id).await.unwrap().text, "External");
    let refreshed = store.read_description(&id).await.unwrap();
    store
        .save_description(&id, "Merged".into(), refreshed.revision)
        .await
        .unwrap();
}

#[tokio::test]
async fn failed_description_writes_preserve_text_and_allow_retry() {
    let (fixture, store, id) = setup().await;
    let empty = store.read_description(&id).await.unwrap();
    let saved = store
        .save_description(&id, "Original".into(), empty.revision)
        .await
        .unwrap();
    let path = fixture.root.join(format!("planner/descriptions/{id}.md"));
    let fault = crate::paths::fail_writes(&path);
    store
        .save_description(&id, "Changed".into(), saved.revision.clone())
        .await
        .unwrap_err();
    assert_eq!(store.read_description(&id).await.unwrap().text, "Original");
    drop(fault);
    store
        .save_description(&id, "Changed".into(), saved.revision)
        .await
        .unwrap();
}

#[tokio::test]
async fn invalid_ids_and_oversized_descriptions_cannot_write_files() {
    let (_fixture, store, id) = setup().await;
    for invalid in [
        "../outside",
        "",
        "unknown",
        "00000000-0000-0000-0000-000000000000",
    ] {
        store.read_description(invalid).await.unwrap_err();
        store
            .save_description(invalid, "Text".into(), revision(""))
            .await
            .unwrap_err();
    }
    for text in ["a".repeat(MAX_DESCRIPTION_BYTES + 1), "\0".into()] {
        store
            .save_description(&id, text, revision(""))
            .await
            .unwrap_err();
    }
    assert!(store.read_description(&id).await.unwrap().text.is_empty());
}
