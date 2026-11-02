use super::*;
#[test]
fn missing_empty_and_deleted_history_start_fresh() {
    let root = std::env::temp_dir().join(format!("slopd-resume-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&root).unwrap();
    let path = root.as_path().join("sessions");
    assert!(!conversation_files(&path, 0, &mut 100));
    std::fs::create_dir(&path).unwrap();
    std::fs::write(path.join("empty.jsonl"), "").unwrap();
    std::fs::write(path.join("config.json"), "{}").unwrap();
    assert!(!conversation_files(&path, 0, &mut 100));
    std::fs::write(path.join("conversation.jsonl"), "{}\n").unwrap();
    assert!(conversation_files(&path, 0, &mut 100));
    std::fs::remove_file(path.join("conversation.jsonl")).unwrap();
    assert!(!conversation_files(&path, 0, &mut 100));
    std::fs::remove_dir_all(root).unwrap();
}
#[test]
fn transcript_links_are_not_followed() {
    let root = std::env::temp_dir().join(format!("slopd-resume-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&root).unwrap();
    std::fs::write(root.as_path().join("outside.jsonl"), "{}\n").unwrap();
    let path = root.as_path().join("sessions");
    std::fs::create_dir(&path).unwrap();
    std::os::unix::fs::symlink(
        root.as_path().join("outside.jsonl"),
        path.join("link.jsonl"),
    )
    .unwrap();
    assert!(!conversation_files(&path, 0, &mut 100));
    std::fs::remove_dir_all(root).unwrap();
}
