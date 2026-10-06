use super::*;

#[test]
fn summary_cache_round_trips_without_storing_the_prompt() {
    let path = std::env::temp_dir().join(format!("slopd-title-cache-{}.toml", std::process::id()));
    drop(fs::remove_file(&path));

    let cache = SummaryCache::load(path.clone());
    cache.insert(
        "codex",
        "fix the parser",
        "Summarise in six words.",
        "test/model",
        "Fix parser",
    );
    cache.flush().unwrap();
    let raw = fs::read_to_string(&path).unwrap();
    assert!(!raw.contains("fix the parser"));
    assert_eq!(
        cache
            .get("fix the parser", "Summarise in six words.", "test/model")
            .as_deref(),
        Some("Fix parser")
    );
    assert_eq!(cache.latest("codex").as_deref(), Some("Fix parser"));

    let restored = SummaryCache::load(path.clone());
    assert_eq!(
        restored
            .get("fix the parser", "Summarise in six words.", "test/model")
            .as_deref(),
        Some("Fix parser")
    );
    assert_eq!(restored.latest("codex").as_deref(), Some("Fix parser"));
    assert!(
        restored
            .get("fix the parser", "Summarise in six words.", "other/model")
            .is_none()
    );
    assert!(
        restored
            .get(
                "fix the parser",
                "Use a different instruction.",
                "test/model"
            )
            .is_none()
    );

    cache.clear_latest("codex");
    cache.flush().unwrap();
    assert!(cache.latest("codex").is_none());
    let cleared = SummaryCache::load(path.clone());
    assert!(cleared.latest("codex").is_none());
    assert_eq!(
        cleared
            .get("fix the parser", "Summarise in six words.", "test/model")
            .as_deref(),
        Some("Fix parser")
    );
    drop(cache);
    drop(fs::remove_file(path));
}

#[test]
fn task_cache_insert_preserves_latest_and_evicts_oldest_entry() {
    let path = std::env::temp_dir().join(format!(
        "slopd-title-task-cache-{}.toml",
        std::process::id()
    ));
    drop(fs::remove_file(&path));

    let cache = SummaryCache::load(path.clone());
    cache.insert(
        "codex",
        "session prompt",
        "Summarise in six words.",
        "test/model",
        "Session title",
    );
    for index in 0..=MAX_CACHE_ENTRIES {
        let prompt = format!("task-{index}");
        let title = format!("Task {index}");
        cache.insert_cached(&prompt, "Summarise in six words.", "test/model", &title);
    }

    assert_eq!(cache.latest("codex").as_deref(), Some("Session title"));
    assert!(
        cache
            .get("task-0", "Summarise in six words.", "test/model")
            .is_none()
    );
    assert_eq!(
        cache
            .get(
                &format!("task-{MAX_CACHE_ENTRIES}"),
                "Summarise in six words.",
                "test/model"
            )
            .as_deref(),
        Some("Task 1024")
    );

    drop(cache);
    drop(fs::remove_file(path));
}

fn cache_test_path() -> PathBuf {
    std::env::temp_dir()
        .join(format!("slopd-summary-{}", uuid::Uuid::new_v4()))
        .join("cache.toml")
}

#[test]
fn cache_key_frames_fields_containing_nul() {
    assert_ne!(
        cache_key("c", "a\0b", "model"),
        cache_key("b\0c", "a", "model")
    );
    assert!(cache_key("prompt", "summary", "model").starts_with("v2-"));
}

#[test]
fn slow_disk_does_not_block_mutations_and_latest_snapshot_wins() {
    use std::sync::mpsc;
    use std::time::Duration;
    let path = cache_test_path();
    let (started_tx, started_rx) = mpsc::channel();
    let (resume_tx, resume_rx) = mpsc::channel();
    let first = std::sync::atomic::AtomicBool::new(true);
    let cache = SummaryCache::load_with_writer(path.clone(), move |path, state| {
        if first.swap(false, std::sync::atomic::Ordering::Relaxed) {
            started_tx.send(()).unwrap();
            resume_rx
                .recv_timeout(Duration::from_secs(5))
                .expect("resume cache writer");
        }
        save_cache(path, state)
    });
    cache.remember("agent", "Old result");
    started_rx.recv_timeout(Duration::from_secs(5)).unwrap();

    // Run mutations on another thread so a lock regression reaches the deadline.
    let (done_tx, done_rx) = mpsc::channel();
    let mutation = std::thread::spawn(move || {
        cache.remember("agent", "New conversation");
        cache.rename_latest("agent", "renamed", Some("New conversation"));
        assert!(cache.latest("agent").is_none());
        assert_eq!(cache.latest("renamed").as_deref(), Some("New conversation"));
        cache.clear_latest("renamed");
        cache.clear_latest("renamed");
        cache.insert_cached("a private prompt", "instruction", "model", "Reusable");
        done_tx.send(cache).unwrap();
    });
    let result = done_rx.recv_timeout(Duration::from_secs(2));
    resume_tx.send(()).unwrap();
    let cache = result.expect("cache mutations blocked on the writer");
    mutation.join().unwrap();
    cache.flush().unwrap();
    let restored = SummaryCache::load(path.clone());
    assert!(restored.latest("agent").is_none());
    assert!(restored.latest("renamed").is_none());
    assert_eq!(
        restored
            .get("a private prompt", "instruction", "model")
            .as_deref(),
        Some("Reusable")
    );
    assert!(
        !fs::read_to_string(&path)
            .unwrap()
            .contains("a private prompt")
    );
    drop(cache);
    drop(restored);
    fs::remove_dir_all(path.parent().unwrap()).unwrap();
}

#[test]
fn failed_writes_keep_memory_and_recover_on_the_next_mutation() {
    let path = cache_test_path();
    let parent = path.parent().unwrap();
    fs::write(parent, "blocks the cache directory").unwrap();
    let cache = SummaryCache::load(path.clone());
    cache.remember("agent", "In memory");
    assert!(cache.flush().is_err());
    assert_eq!(cache.latest("agent").as_deref(), Some("In memory"));
    fs::remove_file(parent).unwrap();
    cache.remember("agent", "Recovered");
    cache.flush().unwrap();
    let restored = SummaryCache::load(path.clone());
    assert_eq!(restored.latest("agent").as_deref(), Some("Recovered"));
    drop(cache);
    drop(restored);
    fs::remove_dir_all(parent).unwrap();
}

#[test]
fn failed_summary_writes_do_not_retry_on_flush_or_drop() {
    use std::sync::atomic::{AtomicUsize, Ordering};
    let attempts = std::sync::Arc::new(AtomicUsize::new(0));
    let writer_attempts = attempts.clone();
    let cache = SummaryCache::load_with_writer(cache_test_path(), move |_, _| {
        writer_attempts.fetch_add(1, Ordering::SeqCst);
        anyhow::bail!("controlled write failure")
    });
    cache.remember("agent", "In memory");
    cache.flush().unwrap_err();
    cache.flush().unwrap_err();
    assert_eq!(attempts.load(Ordering::SeqCst), 1);
    assert_eq!(cache.latest("agent").as_deref(), Some("In memory"));

    cache.remember("agent", "New mutation");
    cache.flush().unwrap_err();
    assert_eq!(attempts.load(Ordering::SeqCst), 2);
    drop(cache);
    assert_eq!(attempts.load(Ordering::SeqCst), 2);
}

#[test]
fn dropping_cache_drains_pending_rename_and_clear() {
    use std::sync::mpsc;
    use std::time::Duration;
    let path = cache_test_path();
    let (started_tx, started_rx) = mpsc::channel();
    let (resume_tx, resume_rx) = mpsc::channel();
    let first = std::sync::atomic::AtomicBool::new(true);
    let cache = SummaryCache::load_with_writer(path.clone(), move |path, state| {
        if first.swap(false, std::sync::atomic::Ordering::Relaxed) {
            started_tx.send(()).unwrap();
            resume_rx.recv().unwrap();
        }
        save_cache(path, state)
    });
    cache.remember("old", "Old title");
    started_rx.recv_timeout(Duration::from_secs(5)).unwrap();
    cache.rename_latest("old", "new", Some("Renamed title"));
    cache.remember("discard", "Temporary");
    cache.clear_latest("discard");
    let (drop_started_tx, drop_started_rx) = mpsc::channel();
    let (dropped_tx, dropped_rx) = mpsc::channel();
    let drop_thread = std::thread::spawn(move || {
        drop_started_tx.send(()).unwrap();
        drop(cache);
        dropped_tx.send(()).unwrap();
    });
    drop_started_rx
        .recv_timeout(Duration::from_secs(5))
        .unwrap();
    assert!(dropped_rx.recv_timeout(Duration::from_millis(100)).is_err());
    resume_tx.send(()).unwrap();
    dropped_rx.recv_timeout(Duration::from_secs(5)).unwrap();
    drop_thread.join().unwrap();
    let restored = SummaryCache::load(path.clone());
    assert_eq!(restored.latest("new").as_deref(), Some("Renamed title"));
    assert!(restored.latest("old").is_none());
    assert!(restored.latest("discard").is_none());
    drop(restored);
    fs::remove_dir_all(path.parent().unwrap()).unwrap();
}
