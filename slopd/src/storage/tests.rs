use super::record::Document;
use super::target::{StorageBinding, Target};
use serde::{Deserialize, Serialize};

#[test]
fn record_edits_preserve_extensions_but_clear_known_defaults_and_optional_fields() {
    #[derive(Serialize, Deserialize)]
    struct Record {
        name: String,
        #[serde(default, skip_serializing_if = "Option::is_none")]
        label: Option<String>,
        #[serde(default, skip_serializing_if = "Vec::is_empty")]
        tags: Vec<String>,
        nested: Nested,
    }
    #[derive(Serialize, Deserialize)]
    struct Nested {
        value: u32,
    }
    let (mut record, document) = Document::decode::<Record>(
        "name = 'before'\nlabel = 'clear'\ntags = []\nextra = 'keep'\n[nested]\nvalue = 1\nfuture = 'keep too'\n"
    ).unwrap();
    record.name = "after".into();
    record.label = None;
    record.nested.value = 2;
    let text = document
        .prepare(
            &record,
            &[&["name"], &["label"], &["tags"], &["nested", "value"]],
        )
        .unwrap();
    let raw: toml::Value = toml::from_str(&text).unwrap();
    assert_eq!(raw["extra"].as_str(), Some("keep"));
    assert_eq!(raw["nested"]["future"].as_str(), Some("keep too"));
    assert_eq!(raw["nested"]["value"].as_integer(), Some(2));
    assert!(raw.get("label").is_none());
    assert!(raw.get("tags").is_none());
    Document::decode::<Record>("name = [")
        .map(|_| ())
        .unwrap_err();
    document.prepare(&record, &[&[]]).unwrap_err();
}

#[test]
fn resolved_bindings_follow_independent_overrides() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    crate::test_support::set_env("SLOPD_CONFIG_ROOT", root.join("config"));
    crate::test_support::set_env("SLOPD_DATA", root.join("data"));
    let binding = StorageBinding::resolved(&root.join("custom/settings.toml")).unwrap();
    assert_eq!(binding.config, root.join("config"));
    assert_eq!(binding.data, root.join("data"));
    assert_eq!(
        Target::Settings.resolve(&binding).unwrap(),
        root.join("custom/settings.toml")
    );
    for target in [
        Target::Config("../escape.toml".into()),
        Target::Data("tasks/../escape.toml".into()),
        Target::Data("agents/not-an-id.toml".into()),
        Target::Config("tasks.toml".into()),
        Target::Data("tasks.journal".into()),
    ] {
        target.resolve(&binding).unwrap_err();
    }
    for target in [
        Target::Config("projects/0123456789abcdef.toml".into()),
        Target::Config("prompts/a prompt.toml".into()),
        Target::Data("agents/1111111111114111.toml".into()),
        Target::Data("tasks/0123456789abcdef.toml".into()),
    ] {
        target.resolve(&binding).unwrap();
    }
}
