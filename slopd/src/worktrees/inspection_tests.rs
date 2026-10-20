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
