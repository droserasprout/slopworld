use super::reader_stat;
use std::path::PathBuf;

#[test]
fn reader_stamp_changes_when_only_the_inode_changes() {
    let original = super::ReaderStamp {
        device: 1,
        inode: 2,
        length: 3,
        mtime: 4,
        mtime_nsec: 5,
        ctime: 6,
        ctime_nsec: 7,
    };
    let replacement = super::ReaderStamp {
        inode: 8,
        ..original
    };

    assert_ne!(original.encode(), replacement.encode());
}

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

    // An atomic replacement with the same size and mtime must still change the stamp.
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
}

#[tokio::test]
async fn search_record_reader_stops_at_its_byte_limit() {
    let mut exact = tokio::io::BufReader::new(&b"1234\n"[..]);
    assert!(matches!(
        super::read_search_record(&mut exact, 5).await.unwrap(),
        Some(super::SearchRecord::Line(record)) if record.as_slice() == b"1234\n"
    ));

    let mut oversized = tokio::io::BufReader::new(&b"12345\n"[..]);
    assert!(matches!(
        super::read_search_record(&mut oversized, 5).await.unwrap(),
        Some(super::SearchRecord::TooLarge)
    ));

    let mut unterminated = tokio::io::BufReader::new(&b"12345"[..]);
    assert!(matches!(
        super::read_search_record(&mut unterminated, 4)
            .await
            .unwrap(),
        Some(super::SearchRecord::TooLarge)
    ));
}

#[tokio::test]
async fn cancelled_highlight_removes_its_temporary_file() {
    let dir = PathBuf::from(crate::paths::temp_dir("highlight"));
    tokio::fs::create_dir_all(&dir).await.unwrap();
    let marker = format!("cancelled-highlight-{}", uuid::Uuid::new_v4());
    let task_marker = marker.clone();
    let mut tasks = tokio::task::JoinSet::new();
    tasks.spawn(async move { super::highlight_text("sleep 30", "txt", &task_marker).await });

    let deadline = tokio::time::Instant::now() + std::time::Duration::from_secs(3);
    let mut temporary = None;
    while tokio::time::Instant::now() < deadline {
        let mut entries = tokio::fs::read_dir(&dir).await.unwrap();
        while let Some(entry) = entries.next_entry().await.unwrap() {
            if tokio::fs::read(entry.path()).await.ok().as_deref() == Some(marker.as_bytes()) {
                temporary = Some(entry.path());
                break;
            }
        }
        if temporary.is_some() {
            break;
        }
        tokio::time::sleep(std::time::Duration::from_millis(10)).await;
    }

    tasks.abort_all();
    let cancelled = tasks
        .join_next()
        .await
        .expect("highlight task should finish");
    assert!(matches!(cancelled, Err(error) if error.is_cancelled()));
    let temporary = temporary.expect("highlight input file should be written before cancellation");
    assert!(!temporary.exists());
}
