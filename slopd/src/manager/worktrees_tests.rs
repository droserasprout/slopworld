use super::*;

#[tokio::test]
async fn view_index_reloads_external_catalog_edits() {
    let manager = crate::session::test_manager(Config::default());
    assert!(manager.worktree_view_index().await.is_empty());
    let catalog = |id: &str, name: &str| {
        toml::to_string(&Store {
            worktrees: vec![Worktree {
                id: id.into(),
                name: name.into(),
                ..Default::default()
            }],
        })
        .unwrap()
    };
    let current = manager.cfg_path.with_file_name("worktrees.toml");
    tokio::fs::write(&current, catalog("new", "current"))
        .await
        .unwrap();
    let index = manager.worktree_view_index().await;
    assert_eq!(index["new"].name, "current");
    tokio::fs::write(&current, catalog("new", "external edit"))
        .await
        .unwrap();
    assert_eq!(
        manager.worktree_view_index().await["new"].name,
        "external edit"
    );
}
