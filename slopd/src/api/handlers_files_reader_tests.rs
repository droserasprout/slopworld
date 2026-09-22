use super::reader_is_file;

#[tokio::test]
async fn reader_metadata_distinguishes_missing_files_from_read_errors() {
    let dir = super::super::tests::fixture("reader-metadata");
    let file = dir.join("hidden.md");
    tokio::fs::write(&file, "hello").await.unwrap();
    assert!(reader_is_file(&file).await.unwrap());
    assert!(!reader_is_file(&dir).await.unwrap());
    assert!(!reader_is_file(&file.join("child")).await.unwrap());
    tokio::fs::remove_file(&file).await.unwrap();
    assert!(!reader_is_file(&file).await.unwrap());
    // A symlink loop is an I/O error, never evidence to dismiss a pinned tab.
    #[cfg(unix)]
    {
        std::os::unix::fs::symlink("loop", dir.join("loop")).unwrap();
        assert!(reader_is_file(&dir.join("loop")).await.is_err());
    }
    tokio::fs::remove_dir_all(&dir).await.unwrap();
}
