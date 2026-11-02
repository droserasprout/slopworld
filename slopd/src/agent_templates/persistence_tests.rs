//! Compatibility of committed catalog indexes across daemon restarts.

use super::*;

#[tokio::test]
async fn supplied_catalog_marker_preserves_templates_and_version_cursor() {
    let root = std::env::temp_dir().join(format!("slopd-template-marker-{}", uuid::Uuid::new_v4()));
    let mut store = AgentTemplateStore::default();
    let template = store
        .create(toml::from_str("name = 'personal'\n[defaults]\ncmd = 'agent'\n").unwrap())
        .unwrap();
    store.next_version = 42;
    store.save(&root).await.unwrap();
    let index = root.join(INDEX_FILE);
    let text = std::fs::read_to_string(&index).unwrap();
    std::fs::write(&index, format!("{text}supplied_version = 1\n")).unwrap();

    let loaded = AgentTemplateStore::load(&root).await.unwrap();
    assert_eq!(loaded.next_version, 42);
    assert_eq!(loaded.templates.len(), 1);
    assert_eq!(
        toml::to_string(loaded.get("personal").unwrap()).unwrap(),
        toml::to_string(&template).unwrap()
    );
    loaded.save(&root).await.unwrap();
    assert!(
        !std::fs::read_to_string(&index)
            .unwrap()
            .contains("supplied_version")
    );
    assert_eq!(
        AgentTemplateStore::load(&root).await.unwrap().next_version,
        42
    );
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn unknown_index_fields_still_fail() {
    toml::from_str::<TemplateIndex>(
        "next_version = 42\ngeneration = 'generation-example'\nunknown = 1\n",
    )
    .map(|_| ())
    .expect_err("unknown index fields must be rejected");
}
