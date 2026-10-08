use super::*;

const TREE: &str = "1111111111111111";
const PROJECT: &str = "2222222222222222";
const SIBLING: &str = "3333333333333333";

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
            phase: "ready".into(),
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
            phase: "ready".into(),
            ..Default::default()
        }],
    };
    manager.save_worktrees(&store).await.unwrap();
    let path = manager
        .cfg_path
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
            phase: "ready".into(),
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
    let cfg = manager
        .config_for_action_scope("p", TREE, file)
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
    store.worktrees[0].phase = "removing".into();
    manager.save_worktrees(&store).await.unwrap();
    manager
        .file_action_command("p", TREE, file, "pwd", true)
        .await
        .unwrap_err();
    store.worktrees[0].phase = "ready".into();
    manager.save_worktrees(&store).await.unwrap();
    assert_eq!(
        manager
            .file_action_command("p", TREE, file, "pwd", true)
            .await
            .unwrap(),
        "pwd"
    );
    std::fs::remove_dir_all(tree).unwrap();
    manager
        .file_action_command("p", TREE, file, "pwd", true)
        .await
        .unwrap_err();
}

#[tokio::test]
async fn allocation_record_failure_removes_only_newly_created_directories() {
    let manager = crate::session::test_manager(Config::default());
    let parent = manager.cfg_path.parent().unwrap().join("checkouts");
    let _fault = crate::paths::fail_writes(
        &manager
            .cfg_path
            .parent()
            .unwrap()
            .join(format!("data/worktrees/{TREE}.toml")),
    );
    let prepared = PreparedWorktree {
        root: PathBuf::from("/tmp"),
        project: ProjectCfg::default(),
        branch: "new".into(),
        base: "base".into(),
        worktree: Worktree {
            id: TREE.into(),
            project_id: PROJECT.into(),
            repository: "/tmp/.git".into(),
            phase: "allocating".into(),
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

#[tokio::test]
async fn managed_destination_rejects_symlink_parents_and_protected_paths() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let project = root.join("project");
    std::fs::create_dir(&project).unwrap();
    let alias = root.join("alias");
    std::os::unix::fs::symlink(&project, &alias).unwrap();
    assert_eq!(
        checked_worktree_destination(&alias, "tree")
            .await
            .unwrap_err()
            .to_string(),
        "worktree project directory cannot be a symlink"
    );
    let accepted = checked_worktree_destination(&project, "tree")
        .await
        .unwrap();
    assert_eq!(accepted, project.canonicalize().unwrap().join("tree"));
    assert!(!accepted.exists(), "validation must not create directories");
    std::fs::create_dir_all(root.join("data")).unwrap();
    assert!(
        checked_worktree_destination(&root.join("data"), "sessions")
            .await
            .unwrap_err()
            .to_string()
            .contains("worktree reaches session private state")
    );
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
    let checkout = manager.cfg_path.parent().unwrap().join("external");
    std::fs::create_dir(&checkout).unwrap();
    std::fs::write(checkout.join("keep"), "external data").unwrap();
    let store = Store {
        worktrees: vec![Worktree {
            id: TREE.into(),
            name: "external".into(),
            repository: "/tmp/.git".into(),
            project_id: project.id,
            path: checkout.to_string_lossy().into_owned(),
            phase: "ready".into(),
            ..Default::default()
        }],
    };
    manager.save_worktrees(&store).await.unwrap();
    let catalog = manager
        .cfg_path
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

#[tokio::test]
async fn failed_removal_commit_retains_intent_for_recovery_and_retry() {
    let manager = crate::session::test_manager(Config::default());
    let checkout = manager.cfg_path.parent().unwrap().join("deleted");
    std::fs::create_dir(&checkout).unwrap();
    let worktree = Worktree {
        id: TREE.into(),
        project_id: PROJECT.into(),
        name: "deleted".into(),
        repository: "/tmp/.git".into(),
        path: checkout.to_string_lossy().into_owned(),
        phase: "ready".into(),
        managed: true,
        ..Default::default()
    };
    let sibling = Worktree {
        id: SIBLING.into(),
        project_id: PROJECT.into(),
        name: "sibling".into(),
        path: "/tmp/sibling".into(),
        repository: "/tmp/.git".into(),
        phase: "ready".into(),
        ..Default::default()
    };
    let mut removal = WorktreeRemoval {
        project: ProjectCfg::default(),
        worktree: worktree.clone(),
        store: Store {
            worktrees: vec![sibling, worktree],
        },
        index: 1,
    };
    removal.record_intent(&manager).await.unwrap();
    // Simulate Git deleting the checkout before the final catalog write fails.
    std::fs::remove_dir(&checkout).unwrap();
    let fault = crate::paths::fail_writes(
        &manager
            .cfg_path
            .parent()
            .unwrap()
            .join(format!("data/worktrees/{TREE}.toml")),
    );
    removal.finish(&manager, Ok(())).await.unwrap_err();
    let retained = manager.worktree_records();
    assert_eq!(retained.worktrees[0].id, SIBLING);
    assert_eq!(retained.worktrees[0].phase, "ready");
    assert_eq!(retained.worktrees[1].phase, "removing");
    drop(fault);

    manager.recover_worktrees().await.unwrap();
    let recovered = manager.worktree_records();
    assert_eq!(recovered.worktrees[1].phase, "error");
    let mut retry = WorktreeRemoval {
        project: ProjectCfg::default(),
        worktree: recovered.worktrees[1].clone(),
        store: recovered,
        index: 1,
    };
    retry.record_intent(&manager).await.unwrap();
    retry.finish(&manager, Ok(())).await.unwrap();
    let remaining = manager.worktree_records();
    assert_eq!(remaining.worktrees.len(), 1);
    assert_eq!(remaining.worktrees[0].id, SIBLING);
}
