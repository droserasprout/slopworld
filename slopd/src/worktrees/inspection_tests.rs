use super::*;

#[tokio::test]
async fn missing_checkout_does_not_erase_relocation_recovery_details() {
    let path = std::env::temp_dir().join(format!("slopd-missing-{}", uuid::Uuid::new_v4()));
    let mut worktree = Worktree {
        path: path.to_string_lossy().into_owned(),
        phase: WorktreePhase::Relocating,
        error: "restore source from destination".into(),
        ..Default::default()
    };
    let (branch, head) = inspect(&mut worktree).await;
    assert!(branch.is_empty() && head.is_empty());
    assert_eq!(worktree.phase, WorktreePhase::Relocating);
    assert_eq!(worktree.error, "restore source from destination");
    worktree.phase = WorktreePhase::Ready;
    inspect(&mut worktree).await;
    assert_eq!(worktree.phase, WorktreePhase::Error);
    assert!(worktree.error.contains("directory is missing"));
}

#[tokio::test]
async fn removal_cleanliness_preserves_changes_and_large_ignored_inventories() {
    let root = super::super::tests::repo();
    assert!(!has_local_files(&root).await.unwrap());
    std::fs::write(root.join("file"), "changed\n").unwrap();
    assert!(has_local_files(&root).await.unwrap());
    std::fs::write(root.join("file"), "base\n").unwrap();
    let nested = root.join("untracked/nested");
    std::fs::create_dir_all(&nested).unwrap();
    std::fs::write(nested.join("precious"), "keep").unwrap();
    assert!(has_local_files(&root).await.unwrap());
    std::fs::remove_dir_all(root.join("untracked")).unwrap();
    std::fs::write(root.join(".git/info/exclude"), "ignored-*\n").unwrap();
    // Root-level files cannot be collapsed into a single directory entry. Their
    // status exceeds the generic Git capture limit even with normal untracked mode.
    for index in 0..4500 {
        std::fs::write(
            root.join(format!("ignored-{index:04}-{}", "x".repeat(230))),
            "keep",
        )
        .unwrap();
    }
    assert!(
        super::super::git(&root, &["status", "--porcelain=v1", "--ignored"])
            .await
            .unwrap_err()
            .to_string()
            .contains("output exceeded limit")
    );
    assert!(has_local_files(&root).await.unwrap());
    assert_eq!(
        std::fs::read_to_string(root.join(format!("ignored-0000-{}", "x".repeat(230)))).unwrap(),
        "keep"
    );
    std::fs::remove_dir_all(root).unwrap();
}

#[tokio::test]
async fn removal_cleanliness_rejects_failed_git_inspection() {
    let root = std::env::temp_dir().join(format!("slop-missing-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir(&root).unwrap();
    assert!(
        has_local_files(&root)
            .await
            .unwrap_err()
            .to_string()
            .contains("Git status:")
    );
    std::fs::remove_dir(root).unwrap();
}
