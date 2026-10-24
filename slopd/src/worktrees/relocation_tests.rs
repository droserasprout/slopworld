use super::*;
use crate::application::{Application, worktrees::Worktrees};
use crate::config::{Config, ProjectCfg};
use crate::storage::workspace;

#[tokio::test]
async fn partial_batch_failure_restores_earlier_moves_and_retains_recovery_intent() {
    partial_batch_failure(false).await;
}

#[tokio::test]
async fn partial_batch_and_reverse_failure_preserve_checkouts_until_repeated_recovery() {
    partial_batch_failure(true).await;
}

async fn partial_batch_failure(block_reverse: bool) {
    let root = super::super::tests::repo();
    let manager = crate::session::test_manager(Config {
        projects: vec![ProjectCfg {
            id: "1111111111111111".into(),
            name: "repo".into(),
            dir: root.to_string_lossy().into_owned(),
            ..Default::default()
        }],
        ..Default::default()
    });
    let app = Application::from_runtime(&manager);
    let owner = Worktrees::new(&app);
    let (original, moves) = checkouts(&root).await;
    manager.save_worktrees(&original).await.unwrap();
    let mut plan = Relocations::new(original, moves).unwrap();
    // Run synchronously after A's Git repair, before B's move: no timing-dependent sabotage.
    plan.after_first_move = Some(Box::new({
        let root = root.clone();
        move || {
            assert!(!root.join("first").exists());
            assert!(root.join("moved-first/uncommitted").exists());
            std::fs::create_dir(root.join("moved-second")).unwrap();
            std::fs::write(root.join("moved-second/sentinel"), "untouched").unwrap();
            if block_reverse {
                std::fs::create_dir(root.join("first")).unwrap();
                std::fs::write(root.join("first/sentinel"), "untouched").unwrap();
            }
        }
    }));
    let error = plan.execute_pending(&owner).await.unwrap_err().to_string();
    assert!(error.contains("moved-second"), "{error}");
    assert_eq!(error.contains("rollback also failed"), block_reverse);
    let first = if block_reverse {
        "moved-first"
    } else {
        "first"
    };
    assert_registration(&root, first).await;
    let binding = &manager.workspace.configuration.records.binding;
    let pending = workspace::Store::<Worktree>::load(binding)
        .await
        .unwrap()
        .ordered();
    assert_eq!(pending.len(), 2);
    for tree in &pending {
        assert_eq!(tree.phase, WorktreePhase::Relocating);
        let intent = tree.relocation.as_ref().unwrap();
        assert_eq!(intent.source, tree.path);
        assert_eq!(
            intent.destination,
            root.join(format!("moved-{}", tree.name)).to_string_lossy()
        );
    }
    for _ in 0..2 {
        owner.recover_worktrees().await.unwrap();
        assert!(
            manager
                .worktree_records()
                .worktrees
                .iter()
                .all(|tree| tree.phase == WorktreePhase::Relocating)
        );
        assert_eq!(
            std::fs::read_to_string(root.join("moved-second/sentinel")).unwrap(),
            "untouched"
        );
        assert!(root.join(first).join("uncommitted").exists());
    }
    if block_reverse {
        assert!(plan.rollback(&owner).await.is_err());
        assert_eq!(
            std::fs::read_to_string(root.join("first/sentinel")).unwrap(),
            "untouched"
        );
        std::fs::remove_file(root.join("first/sentinel")).unwrap();
        std::fs::remove_dir(root.join("first")).unwrap();
    }
    for _ in 0..2 {
        plan.rollback(&owner).await.unwrap();
        let restored = workspace::Store::<Worktree>::load(binding)
            .await
            .unwrap()
            .ordered();
        for tree in restored {
            assert_eq!(tree.phase, WorktreePhase::Ready);
            assert!(tree.relocation.is_none());
            assert_eq!(tree.path, root.join(&tree.name).to_string_lossy());
            assert_eq!(
                std::fs::read_to_string(Path::new(&tree.path).join("uncommitted")).unwrap(),
                tree.name
            );
        }
        assert!(!root.join("moved-first").exists());
        assert_registration(&root, "first").await;
    }
    std::fs::remove_dir_all(root).unwrap();
}

async fn checkouts(root: &Path) -> (Store, Vec<(usize, Worktree)>) {
    let mut original = Store::default();
    let mut moves = Vec::new();
    for (index, name) in ["first", "second"].into_iter().enumerate() {
        let path = root.join(name);
        std::fs::create_dir(&path).unwrap();
        crate::worktrees::allocate(root, &path, name, "HEAD")
            .await
            .unwrap();
        std::fs::write(path.join("uncommitted"), name).unwrap();
        let tree = Worktree {
            id: format!("{index:016x}"),
            project_id: "1111111111111111".into(),
            name: name.into(),
            path: path.to_string_lossy().into_owned(),
            repository: root.join(".git").to_string_lossy().into_owned(),
            managed: true,
            phase: WorktreePhase::Ready,
            ..Default::default()
        };
        let mut moved = tree.clone();
        moved.path = root
            .join(format!("moved-{name}"))
            .to_string_lossy()
            .into_owned();
        moves.push((index, moved));
        original.worktrees.push(tree);
    }
    (original, moves)
}

async fn assert_registration(root: &Path, first: &str) {
    for name in [first, "second"] {
        assert!(root.join(name).join("uncommitted").exists());
        let registration =
            crate::worktrees::git(&root.join(name), &["rev-parse", "--show-toplevel"])
                .await
                .unwrap();
        assert_eq!(registration, root.join(name).to_string_lossy());
    }
    let registered = crate::worktrees::git(root, &["worktree", "list", "--porcelain"])
        .await
        .unwrap();
    assert!(registered.contains(&format!("worktree {}\n", root.join(first).display())));
    assert!(registered.contains(&format!("worktree {}\n", root.join("second").display())));
}

#[tokio::test]
async fn repair_failure_after_move_restores_files_and_registration_but_retains_intent() {
    failed_repair(super::super::RepairFailure::AfterMove).await;
}

#[tokio::test]
async fn repair_failure_cannot_overwrite_a_recreated_source_directory() {
    failed_repair(super::super::RepairFailure::RestoreDirectory).await;
}

#[tokio::test]
async fn failed_original_registration_repair_preserves_files_and_recovery_intent() {
    failed_repair(super::super::RepairFailure::RestoreRegistration).await;
}

async fn failed_repair(failure: super::super::RepairFailure) {
    use super::super::RepairFailure;
    let root = super::super::tests::repo();
    let manager = crate::session::test_manager(Config::default());
    let app = Application::from_runtime(&manager);
    let owner = Worktrees::new(&app);
    let (original, moves) = checkouts(&root).await;
    manager.save_worktrees(&original).await.unwrap();
    let mut plan = Relocations::new(original, moves).unwrap();
    plan.repair_failure = Some(failure);
    let error = format!("{:#}", plan.execute_pending(&owner).await.unwrap_err());
    let expected = match failure {
        RepairFailure::AfterMove => "injected repair failure after move",
        RepairFailure::RestoreDirectory => "restoring checkout after failed Git repair",
        RepairFailure::RestoreRegistration => "repairing original Git worktree after failed move",
    };
    assert!(error.contains(expected), "{error}");
    let retained = if matches!(failure, RepairFailure::RestoreDirectory) {
        assert_eq!(
            std::fs::read_to_string(root.join("first/sentinel")).unwrap(),
            "do not overwrite"
        );
        "moved-first"
    } else {
        assert!(!root.join("moved-first").exists());
        "first"
    };
    assert_eq!(
        std::fs::read_to_string(root.join(retained).join("uncommitted")).unwrap(),
        "first"
    );
    assert_eq!(
        std::fs::read_to_string(root.join("second/uncommitted")).unwrap(),
        "second"
    );
    let registration = std::fs::read_to_string(root.join(".git/worktrees/first/gitdir")).unwrap();
    let registered = if matches!(failure, RepairFailure::AfterMove) {
        "first"
    } else {
        "moved-first"
    };
    assert_eq!(
        registration.trim(),
        root.join(registered).join(".git").to_str().unwrap()
    );
    if !matches!(failure, RepairFailure::RestoreRegistration) {
        assert_registration(&root, retained).await;
    }
    // Recovery must not guess which side of an interrupted repair is authoritative.
    for _ in 0..2 {
        owner.recover_worktrees().await.unwrap();
        let pending =
            workspace::Store::<Worktree>::load(&manager.workspace.configuration.records.binding)
                .await
                .unwrap()
                .ordered();
        for tree in pending {
            assert_eq!(tree.phase, WorktreePhase::Relocating);
            let intent = tree.relocation.unwrap();
            assert_eq!(intent.source, root.join(&tree.name).to_string_lossy());
            assert_eq!(
                intent.destination,
                root.join(format!("moved-{}", tree.name)).to_string_lossy()
            );
        }
        assert!(root.join(retained).join("uncommitted").exists());
    }
    std::fs::remove_dir_all(root).unwrap();
}
