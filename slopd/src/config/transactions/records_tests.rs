//! Accepted worktree readers observe publication, never candidates or offline edits.
use super::*;

#[tokio::test]
async fn worktree_reader_tracks_commits_and_keeps_failed_or_offline_edits_private() {
    let manager = crate::session::test_manager(Config::default());
    let records = &manager.workspace.configuration.records;
    let reader = records.worktree_catalog();
    let empty = reader.snapshot();
    let row = crate::worktrees::Worktree {
        id: crate::storage_id::draft_identity(),
        project_id: crate::storage_id::draft_identity(),
        name: "checkout".into(),
        path: "/tmp/checkout".into(),
        repository: "/tmp/repo".into(),
        ..Default::default()
    };
    let path = records
        .binding
        .data
        .join("worktrees")
        .join(format!("{}.toml", row.id));
    let mut candidate = crate::worktrees::Store {
        worktrees: vec![row],
    };
    manager
        .workspace
        .configuration
        .save_worktrees(&candidate)
        .await
        .unwrap();
    let accepted = reader.snapshot();
    assert!(empty.worktrees.is_empty());
    assert_eq!(accepted.worktrees[0].name, "checkout");
    candidate.worktrees[0].name = "renamed".into();
    let fault = crate::paths::fail_writes(&path);
    manager
        .workspace
        .configuration
        .save_worktrees(&candidate)
        .await
        .unwrap_err();
    assert_eq!(reader.snapshot().worktrees[0].name, "checkout");
    drop(fault);
    manager
        .workspace
        .configuration
        .save_worktrees(&candidate)
        .await
        .unwrap();
    assert_eq!(reader.snapshot().worktrees[0].name, "renamed");
    assert_eq!(accepted.worktrees[0].name, "checkout");
    std::fs::write(&path, "invalid offline edit [").unwrap();
    assert_eq!(reader.snapshot().worktrees[0].name, "renamed");
    let mut detached = reader.snapshot();
    detached.worktrees.clear();
    assert_eq!(reader.snapshot().worktrees.len(), 1);
}
