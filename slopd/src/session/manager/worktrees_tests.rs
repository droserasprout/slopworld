use super::*;

const TREE: &str = "1111111111111111";
const PROJECT: &str = "2222222222222222";

#[tokio::test]
async fn indexed_config_validation_keeps_worktree_ownership_and_running_move_checks() {
    let socket = crate::test_support::TmuxSocket::new();
    let manager = crate::session::test_manager_with_socket(Config::default(), socket.path.clone());
    let project = ProjectCfg {
        name: "repo".into(),
        id: crate::storage_id::draft_identity(),
        dir: "/tmp".into(),
        ..Default::default()
    };
    let old = Config {
        projects: vec![project.clone()],
        sessions: vec![SessionCfg {
            name: "worker".into(),
            project: project.name.clone(),
            worktree: "main".into(),
            ..Default::default()
        }],
        ..Default::default()
    };
    let mut store = Store {
        worktrees: vec![Worktree {
            id: TREE.into(),
            name: "checkout".into(),
            path: "/tmp/checkout".into(),
            repository: "/tmp/.git".into(),
            phase: crate::worktrees::WorktreePhase::Ready,
            project_id: project.id.clone(),
            ..Default::default()
        }],
    };
    manager.save_worktrees(&store).await.unwrap();
    let mut new = old.clone();
    new.sessions[0].worktree = TREE.into();
    manager.validate_worktree_config(&old, &new).await.unwrap();

    manager
        .tmux
        .spawn(
            "worker",
            "/tmp",
            80,
            24,
            &["sleep".into(), "60".into()],
            false,
        )
        .await
        .unwrap();
    let error = manager
        .validate_worktree_config(&old, &new)
        .await
        .unwrap_err();
    assert!(error.to_string().contains("stop session worker"));
    manager.tmux.kill("worker").await.unwrap();

    store.worktrees[0].project_id = crate::storage_id::draft_identity();
    manager.save_worktrees(&store).await.unwrap();
    let error = manager
        .validate_worktree_config(&old, &new)
        .await
        .unwrap_err();
    assert!(error.to_string().contains("unknown worktree"));

    store.worktrees[0].project_id = project.id;
    manager.save_worktrees(&store).await.unwrap();
    new.sessions.clear();
    manager.validate_worktree_config(&old, &new).await.unwrap();
    new.projects[0].dir = "/changed".into();
    let error = manager
        .validate_worktree_config(&old, &new)
        .await
        .unwrap_err();
    assert!(error.to_string().contains("original checkout directory"));
    new.projects.clear();
    let error = manager
        .validate_worktree_config(&old, &new)
        .await
        .unwrap_err();
    assert!(error.to_string().contains("before removing its identity"));
}

#[tokio::test]
async fn view_index_ignores_external_record_edits() {
    let manager = crate::session::test_manager(Config::default());
    let store = Store {
        worktrees: vec![Worktree {
            id: TREE.into(),
            project_id: PROJECT.into(),
            name: "accepted".into(),
            path: "/tmp/checkout".into(),
            repository: "/tmp/.git".into(),
            phase: crate::worktrees::WorktreePhase::Ready,
            ..Default::default()
        }],
    };
    manager.save_worktrees(&store).await.unwrap();
    let path = manager
        .cfg_path()
        .parent()
        .unwrap()
        .join(format!("data/worktrees/{TREE}.toml"));
    for text in ["invalid [", "name = 'external'", ""] {
        std::fs::write(&path, text).unwrap();
        assert_eq!(manager.worktree_view_index().await[TREE].name, "accepted");
    }
    std::fs::remove_file(&path).unwrap();
    assert_eq!(manager.worktree_view_index().await[TREE].name, "accepted");
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
            id: PROJECT.into(),
            name: "p".into(),
            dir: main.to_string_lossy().into(),
            ..Default::default()
        }],
        ..Default::default()
    });
    let store = Store {
        worktrees: vec![Worktree {
            id: TREE.into(),
            project_id: PROJECT.into(),
            name: "one".into(),
            path: tree.to_string_lossy().into(),
            repository: main.join(".git").to_string_lossy().into(),
            phase: crate::worktrees::WorktreePhase::Ready,
            ..Default::default()
        }],
    };
    manager.save_worktrees(&store).await.unwrap();
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
    let cfg = crate::application::file_actions::config_for_action_scope(
        manager.as_ref(),
        "p",
        TREE,
        file,
    )
    .await
    .unwrap();
    assert_eq!(Path::new(&cfg.projects[0].dir), tree);
    manager
        .file_action_command("p", TREE, file, "cat {{ absolute_path }}", true)
        .await
        .unwrap();
    manager
        .file_action_command("p", "main", file, "pwd", true)
        .await
        .unwrap_err();
    manager
        .file_action_command("p", TREE, &main.join("file").to_string_lossy(), "pwd", true)
        .await
        .unwrap_err();
    manager
        .file_action_command("p", "unregistered", file, "pwd", true)
        .await
        .unwrap_err();
    manager
        .file_action_command("wrong-project", TREE, file, "pwd", true)
        .await
        .unwrap_err();
    let output = manager
        .file_action("p", TREE, file, "pwd", false)
        .await
        .unwrap();
    assert_eq!(Path::new(output.trim()), tree);

    assert_cache_allocation_recovery(manager, root, tree, &store.worktrees[0]).await;

    let outside = root.join("outside");
    std::fs::create_dir(&outside).unwrap();
    std::os::unix::fs::symlink(&outside, tree.join("escape")).unwrap();
    manager
        .file_action_command(
            "p",
            TREE,
            &tree.join("escape/new").to_string_lossy(),
            "pwd",
            true,
        )
        .await
        .unwrap_err();
    store.worktrees[0].phase = crate::worktrees::WorktreePhase::Removing;
    manager.save_worktrees(&store).await.unwrap();
    manager
        .file_action_command("p", TREE, file, "pwd", true)
        .await
        .unwrap_err();
    store.worktrees[0].phase = crate::worktrees::WorktreePhase::Ready;
    manager.save_worktrees(&store).await.unwrap();
    assert_eq!(
        manager
            .file_action_command("p", TREE, file, "pwd", true)
            .await
            .unwrap(),
        "pwd"
    );
    // Missing metadata must not fall back to the parent repository.
    let dotgit = std::fs::read(tree.join(".git")).unwrap();
    std::fs::remove_file(tree.join(".git")).unwrap();
    crate::application::file_actions::config_for_action_scope(manager.as_ref(), "p", TREE, file)
        .await
        .unwrap_err();
    // A standalone replacement also has the wrong repository identity.
    assert!(
        std::process::Command::new("git")
            .arg("-C")
            .arg(tree)
            .args(["init", "-q"])
            .status()
            .unwrap()
            .success()
    );
    crate::application::file_actions::config_for_action_scope(manager.as_ref(), "p", TREE, file)
        .await
        .unwrap_err();
    std::fs::remove_dir_all(tree.join(".git")).unwrap();
    std::fs::write(tree.join(".git"), dotgit).unwrap();
    std::fs::remove_dir_all(tree).unwrap();
    manager
        .file_action_command("p", TREE, file, "pwd", true)
        .await
        .unwrap_err();
}

#[tokio::test]
async fn failed_removal_intent_preserves_external_checkout_and_catalog() {
    let project = ProjectCfg {
        name: "repo".into(),
        id: crate::storage_id::draft_identity(),
        dir: "/tmp".into(),
        ..Default::default()
    };
    let manager = crate::session::test_manager(Config {
        projects: vec![project.clone()],
        ..Default::default()
    });
    let checkout = manager.cfg_path().parent().unwrap().join("external");
    std::fs::create_dir(&checkout).unwrap();
    std::fs::write(checkout.join("keep"), "external data").unwrap();
    let store = Store {
        worktrees: vec![Worktree {
            id: TREE.into(),
            name: "external".into(),
            repository: "/tmp/.git".into(),
            project_id: project.id,
            path: checkout.to_string_lossy().into_owned(),
            phase: crate::worktrees::WorktreePhase::Ready,
            ..Default::default()
        }],
    };
    manager.save_worktrees(&store).await.unwrap();
    let catalog = manager
        .cfg_path()
        .parent()
        .unwrap()
        .join(format!("data/worktrees/{TREE}.toml"));
    let before = std::fs::read(&catalog).unwrap();
    let fault = crate::paths::fail_writes(&catalog);
    manager
        .remove_worktree("repo".into(), TREE.into())
        .await
        .unwrap_err();
    assert_eq!(std::fs::read(&catalog).unwrap(), before);
    drop(fault);

    manager
        .remove_worktree("repo".into(), TREE.into())
        .await
        .unwrap();
    assert!(manager.worktree_records().worktrees.is_empty());
    assert_eq!(
        std::fs::read_to_string(checkout.join("keep")).unwrap(),
        "external data"
    );
    manager
        .remove_worktree("repo".into(), TREE.into())
        .await
        .unwrap_err();
    assert!(checkout.join("keep").exists());
}

async fn assert_cache_allocation_recovery(
    manager: &crate::session::Manager,
    root: &Path,
    tree: &Path,
    record: &Worktree,
) {
    let cache = root.join("cache");
    std::fs::create_dir(&cache).unwrap();
    manager.config_authority.write().await.projects[0]
        .mounts
        .push(crate::config::Mount {
            from: cache.to_string_lossy().into_owned(),
            to: "cache-link".into(),
            mode: crate::config::MountMode::Cache,
        });
    let mut allocating = record.clone();
    allocating.base = git(tree, &["rev-parse", "HEAD"]).await.unwrap();
    std::os::unix::fs::symlink(&cache, tree.join("cache-link")).unwrap();
    assert!(
        crate::worktrees::resolution::completed_allocation(&manager.config().await, &allocating)
            .await
            .unwrap()
    );
    std::fs::write(tree.join("unrelated"), "preserve").unwrap();
    assert!(
        !crate::worktrees::resolution::completed_allocation(&manager.config().await, &allocating)
            .await
            .unwrap()
    );
    std::fs::remove_file(tree.join("unrelated")).unwrap();
    std::fs::remove_file(tree.join("cache-link")).unwrap();
    std::os::unix::fs::symlink(root, tree.join("cache-link")).unwrap();
    assert!(
        !crate::worktrees::resolution::completed_allocation(&manager.config().await, &allocating)
            .await
            .unwrap()
    );
    std::fs::remove_file(tree.join("cache-link")).unwrap();
    manager.config_authority.write().await.projects[0]
        .mounts
        .clear();
}
