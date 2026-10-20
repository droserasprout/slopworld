use super::*;

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
