use super::*;

#[test]
fn rollback_removes_only_empty_directories_created_by_this_attempt() {
    let root = std::env::temp_dir().join(format!("slop-directories-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir(&root).unwrap();
    {
        let mut directories = CreatedDirectories::default();
        directories.create_all(&root.join("empty/nested")).unwrap();
        directories.create_all(&root.join("occupied")).unwrap();
        std::fs::write(root.join("occupied/file"), "keep").unwrap();
    }
    assert!(!root.join("empty").exists());
    assert!(root.join("occupied/file").is_file());
    assert!(root.exists());
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn committed_paths_survive_repeated_commit_and_later_rollback() {
    let root = std::env::temp_dir().join(format!("slop-directories-{}", uuid::Uuid::new_v4()));
    {
        let mut directories = CreatedDirectories::default();
        directories.create_all(&root.join("committed")).unwrap();
        directories.commit();
        directories.commit();
        directories
            .create_all(&root.join("uncommitted/nested"))
            .unwrap();
    }
    assert!(root.join("committed").is_dir());
    assert!(!root.join("uncommitted").exists());
    std::fs::remove_dir_all(root).unwrap();
}
