use super::reader_stat;

#[tokio::test]
async fn reader_metadata_distinguishes_missing_files_from_read_errors() {
    let dir = super::super::tests::fixture("reader-metadata");
    let file = dir.join("hidden.md");
    tokio::fs::write(&file, "hello").await.unwrap();
    assert!(reader_stat(&file).await.unwrap().is_file);
    assert!(!reader_stat(&dir).await.unwrap().is_file);
    assert!(!reader_stat(&file.join("child")).await.unwrap().is_file);
    tokio::fs::remove_file(&file).await.unwrap();
    assert!(!reader_stat(&file).await.unwrap().is_file);
    // Report a symlink loop as an I/O error without closing the pinned tab.
    #[cfg(unix)]
    {
        std::os::unix::fs::symlink("loop", dir.join("loop")).unwrap();
        assert!(reader_stat(&dir.join("loop")).await.is_err());
    }
    tokio::fs::remove_dir_all(&dir).await.unwrap();
}

#[tokio::test]
async fn reader_stamp_detects_edits_and_atomic_replacement() {
    let dir = super::super::tests::fixture("reader-stamp");
    let file = dir.join("file.txt");
    tokio::fs::write(&file, "old").await.unwrap();
    let initial = reader_stat(&file).await.unwrap();
    assert!(!initial.stamp.is_empty());
    assert_eq!(initial.stamp, reader_stat(&file).await.unwrap().stamp);

    let original_time = std::fs::metadata(&file).unwrap().modified().unwrap();
    tokio::fs::write(&file, "new content").await.unwrap();
    let edited = reader_stat(&file).await.unwrap();
    assert_ne!(initial.stamp, edited.stamp);

    // Atomic saves may preserve size and mtime; file identity must still change the stamp.
    let replacement = dir.join("replacement");
    tokio::fs::write(&replacement, "new content").await.unwrap();
    std::fs::File::open(&file)
        .unwrap()
        .set_modified(original_time)
        .unwrap();
    std::fs::File::open(&replacement)
        .unwrap()
        .set_modified(original_time)
        .unwrap();
    let before_replace = reader_stat(&file).await.unwrap().stamp;
    tokio::fs::rename(&replacement, &file).await.unwrap();
    assert_ne!(before_replace, reader_stat(&file).await.unwrap().stamp);
    assert!(reader_stat(&dir).await.unwrap().stamp.is_empty());
    tokio::fs::remove_dir_all(&dir).await.unwrap();
}
