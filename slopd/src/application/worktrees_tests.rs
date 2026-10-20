use super::*;
const TREE: &str = "1111111111111111";
const PROJECT: &str = "2222222222222222";
const SIBLING: &str = "3333333333333333";
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
            phase: crate::worktrees::WorktreePhase::Allocating,
            name: "new".into(),
            path: parent.join("new").to_string_lossy().into_owned(),
            managed: true,
            ..Default::default()
        },
    };
    Coordinator::new(manager.as_ref())
        .finish_worktree_creation(prepared)
        .await
        .unwrap_err();
    assert!(!parent.exists());
    assert!(manager.cfg_path.parent().unwrap().is_dir());
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
        phase: crate::worktrees::WorktreePhase::Ready,
        managed: true,
        ..Default::default()
    };
    let sibling = Worktree {
        id: SIBLING.into(),
        project_id: PROJECT.into(),
        name: "sibling".into(),
        path: "/tmp/sibling".into(),
        repository: "/tmp/.git".into(),
        phase: crate::worktrees::WorktreePhase::Ready,
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
    removal.record_intent(manager.as_ref()).await.unwrap();
    // Simulate Git deleting the checkout before the final catalog write fails.
    std::fs::remove_dir(&checkout).unwrap();
    let fault = crate::paths::fail_writes(
        &manager
            .cfg_path
            .parent()
            .unwrap()
            .join(format!("data/worktrees/{TREE}.toml")),
    );
    removal.finish(manager.as_ref(), Ok(())).await.unwrap_err();
    let retained = manager.worktree_records();
    assert_eq!(retained.worktrees[0].id, SIBLING);
    assert_eq!(
        retained.worktrees[0].phase,
        crate::worktrees::WorktreePhase::Ready
    );
    assert_eq!(
        retained.worktrees[1].phase,
        crate::worktrees::WorktreePhase::Removing
    );
    drop(fault);

    manager.recover_worktrees().await.unwrap();
    let recovered = manager.worktree_records();
    assert_eq!(
        recovered.worktrees[1].phase,
        crate::worktrees::WorktreePhase::Error
    );
    let mut retry = WorktreeRemoval {
        project: ProjectCfg::default(),
        worktree: recovered.worktrees[1].clone(),
        store: recovered,
        index: 1,
    };
    retry.record_intent(manager.as_ref()).await.unwrap();
    retry.finish(manager.as_ref(), Ok(())).await.unwrap();
    let remaining = manager.worktree_records();
    assert_eq!(remaining.worktrees.len(), 1);
    assert_eq!(remaining.worktrees[0].id, SIBLING);
}
