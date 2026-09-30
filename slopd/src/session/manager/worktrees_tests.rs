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

#[tokio::test]
async fn explicit_action_scope_checks_registration_readiness_and_checkout_path() {
    use crate::config::ProjectCfg;
    let root = std::env::temp_dir().join(format!("slopd-action-scope-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&root).unwrap();
    let main = root.join("main");
    std::fs::create_dir(&main).unwrap();
    assert!(
        std::process::Command::new("git")
            .args(["init", "-q"])
            .arg(&main)
            .status()
            .unwrap()
            .success()
    );
    assert!(
        std::process::Command::new("git")
            .arg("-C")
            .arg(&main)
            .args([
                "-c",
                "user.name=Test",
                "-c",
                "user.email=test@example.invalid",
                "commit",
                "-q",
                "--allow-empty",
                "-m",
                "initial"
            ])
            .status()
            .unwrap()
            .success()
    );
    // A registered child checkout must not be authorized as part of Main just because
    // its absolute path has Main's prefix.
    let tree = main.join("checkouts/one");
    assert!(
        std::process::Command::new("git")
            .arg("-C")
            .arg(&main)
            .args(["worktree", "add", "-q", "-b", "one"])
            .arg(&tree)
            .status()
            .unwrap()
            .success()
    );
    let manager = crate::session::test_manager(Config {
        projects: vec![ProjectCfg {
            id: "p-id".into(),
            name: "p".into(),
            dir: main.to_string_lossy().into(),
            ..Default::default()
        }],
        ..Default::default()
    });
    let store = Store {
        worktrees: vec![Worktree {
            id: "one".into(),
            project_id: "p-id".into(),
            name: "one".into(),
            path: tree.to_string_lossy().into(),
            repository: main.join(".git").to_string_lossy().into(),
            phase: "ready".into(),
            ..Default::default()
        }],
    };
    store.save(&manager.cfg_path).await.unwrap();
    let file = tree.join("file").to_string_lossy().into_owned();
    assert_action_scope_rules(&manager, &root, &main, &tree, &file, store).await;
    std::fs::remove_dir_all(root).unwrap();
}

async fn assert_action_scope_rules(
    manager: &std::sync::Arc<crate::session::Manager>,
    root: &Path,
    main: &Path,
    tree: &Path,
    file: &str,
    mut store: Store,
) {
    let cfg = manager
        .config_for_action_scope("p", "one", file)
        .await
        .unwrap();
    assert_eq!(Path::new(&cfg.projects[0].dir), tree);
    manager
        .file_action_command("p", "one", file, "cat {{ absolute_path }}", true)
        .await
        .unwrap();
    manager
        .file_action_command("p", "main", file, "pwd", true)
        .await
        .unwrap_err();
    manager
        .file_action_command(
            "p",
            "one",
            &main.join("file").to_string_lossy(),
            "pwd",
            true,
        )
        .await
        .unwrap_err();
    manager
        .file_action_command("p", "unregistered", file, "pwd", true)
        .await
        .unwrap_err();
    manager
        .file_action_command("wrong-project", "one", file, "pwd", true)
        .await
        .unwrap_err();
    let output = manager
        .file_action("p", "one", file, "pwd", false)
        .await
        .unwrap();
    assert_eq!(Path::new(output.trim()), tree);

    let outside = root.join("outside");
    std::fs::create_dir(&outside).unwrap();
    std::os::unix::fs::symlink(&outside, tree.join("escape")).unwrap();
    manager
        .file_action_command(
            "p",
            "one",
            &tree.join("escape/new").to_string_lossy(),
            "pwd",
            true,
        )
        .await
        .unwrap_err();
    store.worktrees[0].phase = "removing".into();
    store.save(&manager.cfg_path).await.unwrap();
    manager
        .file_action_command("p", "one", file, "pwd", true)
        .await
        .unwrap_err();
    store.worktrees[0].phase = "ready".into();
    store.save(&manager.cfg_path).await.unwrap();
    assert_eq!(
        manager
            .file_action_command("p", "one", file, "pwd", true)
            .await
            .unwrap(),
        "pwd"
    );
    std::fs::remove_dir_all(tree).unwrap();
    manager
        .file_action_command("p", "one", file, "pwd", true)
        .await
        .unwrap_err();
}

#[tokio::test]
async fn failed_view_load_keeps_valid_rows_and_retries_the_same_file_stamp() {
    let manager = crate::session::test_manager(Config::default());
    let path = manager.cfg_path.with_file_name("worktrees.toml");
    let good = toml::to_string(&Store {
        worktrees: vec![Worktree {
            id: "kept".into(),
            name: "kept".into(),
            ..Default::default()
        }],
    })
    .unwrap();
    std::fs::write(&path, &good).unwrap();
    assert!(manager.worktree_view_index().await.contains_key("kept"));
    // Keep both length and timestamp identical between the failed read and repair.
    std::fs::write(&path, "!".repeat(good.len())).unwrap();
    let stamp = std::fs::metadata(&path).unwrap().modified().unwrap();
    assert!(manager.worktree_view_index().await.contains_key("kept"));
    std::fs::write(&path, &good).unwrap();
    std::fs::File::open(&path)
        .unwrap()
        .set_modified(stamp)
        .unwrap();
    assert!(manager.worktree_view_index().await.contains_key("kept"));
    assert_eq!(
        manager.worktrees.views.lock().await.stamp,
        Some(file_stamp(&path).await)
    );
}

#[tokio::test]
async fn allocation_record_failure_removes_only_newly_created_directories() {
    let manager = crate::session::test_manager(Config::default());
    let parent = manager.cfg_path.parent().unwrap().join("checkouts");
    let _fault = crate::paths::fail_writes(&manager.cfg_path.with_file_name("worktrees.toml"));
    let prepared = PreparedWorktree {
        root: PathBuf::from("/tmp"),
        project: ProjectCfg::default(),
        branch: "new".into(),
        base: "base".into(),
        worktree: Worktree {
            name: "new".into(),
            path: parent.join("new").to_string_lossy().into_owned(),
            managed: true,
            ..Default::default()
        },
    };
    manager
        .finish_worktree_creation(prepared)
        .await
        .unwrap_err();
    assert!(!parent.exists());
    assert!(manager.cfg_path.parent().unwrap().is_dir());
}
