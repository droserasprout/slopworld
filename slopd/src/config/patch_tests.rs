use super::*;

#[test]
fn config_patches_reject_null_at_any_depth() {
    for patch in [
        serde_json::json!({"daemon": {"bind": null}}),
        serde_json::json!({"project": [{"name": "repo"}, null]}),
    ] {
        assert_eq!(
            json_to_toml(patch).unwrap_err().to_string(),
            "The config patch cannot contain null values."
        );
    }
}

#[test]
fn config_patch_conversion_preserves_signed_integers_and_floats() {
    let converted = json_to_toml(serde_json::json!({
        "min": i64::MIN,
        "max": i64::MAX,
        "fraction": 1.25,
    }))
    .unwrap();
    assert_eq!(converted["min"].as_integer(), Some(i64::MIN));
    assert_eq!(converted["max"].as_integer(), Some(i64::MAX));
    assert_eq!(converted["fraction"].as_float(), Some(1.25));
}

#[test]
fn config_patches_replace_arrays_and_preserve_unknown_nested_fields() {
    let mut document: toml::Value = toml::from_str(
        r#"
untouched = "keep"
[[project]]
name = "old"
[daemon]
bind = "127.0.0.1:9999"
[daemon.extension]
enabled = true
"#,
    )
    .unwrap();
    merge_toml(
        &mut document,
        json_to_toml(serde_json::json!({
            "daemon": {"bind": "127.0.0.1:8888", "new_field": 42},
            "project": [],
        }))
        .unwrap(),
    );
    assert_eq!(
        document,
        toml::from_str::<toml::Value>(
            r#"
untouched = "keep"
project = []
[daemon]
bind = "127.0.0.1:8888"
new_field = 42
[daemon.extension]
enabled = true
"#
        )
        .unwrap()
    );
}

#[test]
fn config_patches_reject_integers_above_toml_range() {
    for value in [i64::MAX as u64 + 1, u64::MAX] {
        json_to_toml(serde_json::json!(value)).unwrap_err();
    }
    assert_eq!(
        json_to_toml(serde_json::json!(2_f64.powi(63)))
            .unwrap()
            .as_float(),
        Some(2_f64.powi(63))
    );
}

#[test]
fn config_patches_merge_nested_fields_without_resetting_unmentioned_values() {
    let mut document: toml::Value = toml::from_str(
        r#"
[daemon]
bind = "127.0.0.1:7717"
token = "secret"
future = "keep"

[defaults]
agent = "claude"
shell = "bash"
"#,
    )
    .expect("config parses");

    let patch = json_to_toml(serde_json::json!({
        "daemon": { "usage_poll_secs": 120 },
    }))
    .expect("patch converts");
    merge_toml(&mut document, patch);

    assert_eq!(
        document["daemon"]["usage_poll_secs"].as_integer(),
        Some(120)
    );
    assert_eq!(document["daemon"]["future"].as_str(), Some("keep"));
    assert_eq!(document["defaults"]["agent"].as_str(), Some("claude"));
    assert_eq!(document["daemon"]["token"].as_str(), Some("secret"));
}

#[test]
fn json_patch_conversion_rejects_null_and_preserves_nested_values() {
    let value = json_to_toml(serde_json::json!({
        "enabled": true,
        "count": 3,
        "nested": ["one", false],
    }))
    .unwrap();
    assert_eq!(value["enabled"].as_bool(), Some(true));
    assert_eq!(value["count"].as_integer(), Some(3));
    assert_eq!(value["nested"][0].as_str(), Some("one"));
    assert_eq!(value["nested"][1].as_bool(), Some(false));
    json_to_toml(serde_json::Value::Null).unwrap_err();
}

#[test]
fn a_toml_table_patch_can_replace_a_scalar() {
    let mut base = toml::Value::String("old".into());
    merge_toml(
        &mut base,
        toml::toml! {
            replacement = "new"
        }
        .into(),
    );
    assert_eq!(base["replacement"].as_str(), Some("new"));
}
