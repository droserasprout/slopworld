use super::*;
use crate::{config::ProjectCfg, worktrees::Worktree};

#[test]
fn projects_preserve_order_extensions_and_clear_known_fields() {
    let mut store = Store::<ProjectCfg>::empty();
    let project = ProjectCfg {
        id: "1111111111111111".into(),
        name: "repo".into(),
        dir: "/tmp".into(),
        worktree_root: "/tmp/trees".into(),
        ..Default::default()
    };
    let plan = store.prepare(vec![project.clone()]).unwrap();
    store.publish(plan);
    store
        .entries
        .get_mut(&project.id)
        .unwrap()
        .raw
        .as_table_mut()
        .unwrap()
        .insert("future".into(), "retained".into());
    let mut edited = project.clone();
    edited.name = "renamed".into();
    edited.worktree_root.clear();
    let plan = store.prepare(vec![edited]).unwrap();
    let Mutation::Replace(text) = &plan.changes[0].mutation else {
        panic!("expected replacement")
    };
    let raw: toml::Value = toml::from_str(text).unwrap();
    assert_eq!(raw["future"].as_str(), Some("retained"));
    assert!(raw.get("worktree_root").is_none());
    assert_eq!(raw["storage_order"].as_integer(), Some(0));
    store.publish(plan);
    let plan = store.prepare(vec![]).unwrap();
    store.publish(plan);
    let plan = store.prepare(vec![project]).unwrap();
    assert!(matches!(plan.changes[0].mutation, Mutation::Create(_)));
}

#[test]
fn worktree_validation_rejects_synthetic_main_and_duplicate_checkout_paths() {
    let store = Store::<Worktree>::empty();
    let mut row = Worktree {
        id: "main".into(),
        project_id: "1111111111111111".into(),
        name: "tree".into(),
        path: "/tmp/tree".into(),
        repository: "/tmp/repo/.git".into(),
        phase: "ready".into(),
        ..Default::default()
    };
    assert!(store.prepare(vec![row.clone()]).is_err());
    row.id = uuid::Uuid::new_v4().to_string();
    store.prepare(vec![row.clone()]).unwrap();
    let mut other = row.clone();
    other.id = "2222222222222222".into();
    assert!(store.prepare(vec![row, other]).is_err());
}

#[tokio::test]
async fn discovery_preserves_array_order_and_rejects_bad_identity_order_and_aliases() {
    let root =
        std::env::temp_dir().join(format!("slopd-workspace-records-{}", uuid::Uuid::new_v4()));
    let binding = StorageBinding::new(
        &root.join("config"),
        &root.join("data"),
        &root.join("config/settings.toml"),
    )
    .unwrap();
    let mut values = vec![
        ProjectCfg {
            id: "ffffffffffffffff".into(),
            name: "first".into(),
            dir: "/tmp".into(),
            ..Default::default()
        },
        ProjectCfg {
            id: "1111111111111111".into(),
            name: "second".into(),
            dir: "/tmp".into(),
            ..Default::default()
        },
    ];
    let plan = Store::<ProjectCfg>::empty()
        .prepare(values.clone())
        .unwrap();
    let gate = std::sync::Arc::new(tokio::sync::Mutex::new(()))
        .lock_owned()
        .await;
    super::super::transaction::commit_in_operation(&binding, plan.changes, &gate)
        .await
        .unwrap();
    let loaded = Store::<ProjectCfg>::load(&binding).await.unwrap();
    assert_eq!(
        loaded
            .ordered()
            .iter()
            .map(|p| p.name.as_str())
            .collect::<Vec<_>>(),
        ["first", "second"]
    );
    values.reverse();
    assert!(loaded.prepare(values).is_err());
    let first = project_target("ffffffffffffffff")
        .resolve(&binding)
        .unwrap();
    let original = std::fs::read_to_string(&first).unwrap();
    std::fs::write(
        &first,
        original.replace("storage_order = 0", "storage_order = 1"),
    )
    .unwrap();
    assert!(Store::<ProjectCfg>::load(&binding).await.is_err());
    std::fs::write(
        &first,
        original.replace("ffffffffffffffff", "2222222222222222"),
    )
    .unwrap();
    assert!(Store::<ProjectCfg>::load(&binding).await.is_err());
    std::fs::remove_file(&first).unwrap();
    let outside = root.join("outside.toml");
    std::fs::write(&outside, original).unwrap();
    std::os::unix::fs::symlink(&outside, &first).unwrap();
    assert!(Store::<ProjectCfg>::load(&binding).await.is_err());
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn project_mount_extensions_follow_destination_and_disappear_with_removed_mounts() {
    let raw: toml::Value = toml::from_str(
        r#"
id = "1111111111111111"
name = "repo"
dir = "/tmp/repo"
storage_order = 0
future = "root"
[[mounts]]
from = "/tmp/shared"
to = "shared"
mode = "rw"
future = "mount"
"#,
    )
    .unwrap();
    let mut project = ProjectCfg::decode(&raw).unwrap();
    project.mounts[0].from = "/tmp/changed".into();
    let edited = project.document(0, Some(&raw)).unwrap();
    assert_eq!(edited["mounts"][0]["future"].as_str(), Some("mount"));
    assert_eq!(edited["mounts"][0]["from"].as_str(), Some("/tmp/changed"));
    project.mounts.clear();
    let cleared = project.document(0, Some(&raw)).unwrap();
    assert!(cleared["mounts"].as_array().unwrap().is_empty());
    assert_eq!(cleared["future"].as_str(), Some("root"));
}
