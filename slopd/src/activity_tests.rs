use super::{ActivityCache, CacheEntry, MAX_CACHE_ENTRIES, cache_path, trim_entries};
use crate::session::State;
use std::time::{SystemTime, UNIX_EPOCH};

fn test_path() -> std::path::PathBuf {
    let nonce = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap()
        .as_nanos();
    std::env::temp_dir().join(format!("slopworld-session-activity-{nonce}.toml"))
}

#[test]
fn activity_cache_round_trips_and_clears() {
    let path = test_path();
    assert!(cache_path(&path).ends_with("session-activity.toml"));

    let cache = ActivityCache::load(path.clone());
    cache.remember("agent", State::Waiting, 123).unwrap();
    assert_eq!(cache.get("agent").unwrap().state, State::Waiting);
    assert_eq!(cache.get("agent").unwrap().state_since, 123);

    cache.flush().unwrap();
    let restored = ActivityCache::load(path.clone());
    assert_eq!(restored.get("agent"), cache.get("agent"));

    restored.rename("agent", "renamed").unwrap();
    assert!(restored.get("agent").is_none());
    assert!(restored.get("renamed").is_some());
    restored.clear("renamed").unwrap();
    restored.flush().unwrap();
    assert!(ActivityCache::load(path.clone()).get("renamed").is_none());
    drop(std::fs::remove_file(path));
}

#[test]
fn loaded_entries_filter_invalid_rows_and_keep_last_duplicate() {
    let entries = vec![
        CacheEntry {
            session: "duplicate".into(),
            state: State::Working,
            state_since: 1,
        },
        CacheEntry {
            session: " ".into(),
            state: State::Waiting,
            state_since: 2,
        },
        CacheEntry {
            session: "down".into(),
            state: State::Down,
            state_since: 3,
        },
        CacheEntry {
            session: "duplicate".into(),
            state: State::Idle,
            state_since: 4,
        },
    ];
    let entries = trim_entries(entries);
    assert_eq!(entries.len(), 1);
    assert_eq!(entries[0].session, "duplicate");
    assert_eq!(entries[0].state, State::Idle);
    assert_eq!(entries[0].state_since, 4);
}

#[test]
fn loaded_entries_evict_old_overflow() {
    let entries = trim_entries(
        (0..=MAX_CACHE_ENTRIES)
            .map(|n| CacheEntry {
                session: format!("agent-{n}"),
                state: State::Waiting,
                state_since: n as u64,
            })
            .collect(),
    );
    assert_eq!(entries.len(), MAX_CACHE_ENTRIES);
    assert_eq!(entries.first().unwrap().session, "agent-1");
    assert_eq!(
        entries.last().unwrap().session,
        format!("agent-{MAX_CACHE_ENTRIES}")
    );
}

#[test]
fn down_clears_an_entry_and_rename_replaces_the_destination() {
    let path = test_path();
    let cache = ActivityCache::load(path.clone());
    cache.remember("old", State::Working, 10).unwrap();
    cache.remember("new", State::Waiting, 20).unwrap();

    cache.rename("old", "new").unwrap();
    assert_eq!(cache.get("new").unwrap().state, State::Working);
    assert_eq!(cache.get("new").unwrap().state_since, 10);
    cache.remember("new", State::Down, 30).unwrap();
    assert!(cache.get("new").is_none());
    cache.flush().unwrap();
    assert!(ActivityCache::load(path.clone()).get("new").is_none());

    drop(std::fs::remove_file(path));
}

#[test]
fn invalid_and_future_cache_files_are_ignored() {
    let path = test_path();
    std::fs::write(&path, "not toml at all").unwrap();
    assert!(ActivityCache::load(path.clone()).get("agent").is_none());

    std::fs::write(
        &path,
        "version = 999\n\n[[entries]]\nsession = \"agent\"\nstate = \"working\"\nstate_since = 1\n",
    )
    .unwrap();
    assert!(ActivityCache::load(path.clone()).get("agent").is_none());

    drop(std::fs::remove_file(path));
}

#[test]
fn burst_writer_keeps_latest_rename_clear_and_flushes_on_drop() {
    let path = test_path();
    {
        let cache = ActivityCache::load(path.clone());
        for n in 1..2000 {
            cache.remember("agent", State::Working, n).unwrap();
            cache.remember("removed", State::Waiting, n).unwrap();
        }
        cache.rename("agent", "final").unwrap();
        cache.clear("removed").unwrap();
    }
    let restored = ActivityCache::load(path.clone());
    assert!(restored.get("agent").is_none());
    assert!(restored.get("removed").is_none());
    assert_eq!(restored.get("final").unwrap().state_since, 1999);
    drop(restored);
    drop(std::fs::remove_file(path));
}

#[test]
fn background_write_error_is_reported_and_unchanged_flush_retries() {
    let root = test_path();
    std::fs::write(&root, "not a directory").unwrap();
    let cache = ActivityCache::load(root.join("activity.toml"));
    cache.remember("agent", State::Working, 10).unwrap();
    assert!(cache.flush().is_err());
    std::fs::remove_file(&root).unwrap();
    std::fs::create_dir(&root).unwrap();
    cache.remember("agent", State::Working, 10).unwrap();
    cache.flush().unwrap();
    assert_eq!(
        ActivityCache::load(root.join("activity.toml"))
            .get("agent")
            .unwrap()
            .state_since,
        10
    );
    drop(cache);
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn drop_waits_for_pending_write() {
    let path = test_path();
    let (entered_tx, entered_rx) = std::sync::mpsc::channel();
    let gate = std::sync::Arc::new(std::sync::Barrier::new(2));
    let writer_gate = gate.clone();
    let cache = ActivityCache::load_with_writer(path.clone(), move |path, entries| {
        entered_tx.send(()).unwrap();
        writer_gate.wait();
        super::save_cache(path, entries)
    });
    cache.remember("agent", State::Working, 123).unwrap();
    entered_rx
        .recv_timeout(std::time::Duration::from_secs(5))
        .unwrap();
    let (done_tx, done_rx) = std::sync::mpsc::channel();
    let dropper = std::thread::spawn(move || {
        drop(cache);
        done_tx.send(()).unwrap();
    });
    assert!(
        done_rx
            .recv_timeout(std::time::Duration::from_millis(50))
            .is_err()
    );
    gate.wait();
    done_rx
        .recv_timeout(std::time::Duration::from_secs(5))
        .unwrap();
    dropper.join().unwrap();
    let restored = ActivityCache::load(path.clone());
    assert_eq!(restored.get("agent").unwrap().state_since, 123);
    drop(restored);
    std::fs::remove_file(path).unwrap();
}

#[test]
fn failed_writes_never_advance_durability_and_flush_retries_once() {
    let attempts = std::sync::Arc::new(std::sync::atomic::AtomicUsize::new(0));
    let writer_attempts = attempts.clone();
    let cache = ActivityCache::load_with_writer(test_path(), move |_, _| {
        writer_attempts.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
        anyhow::bail!("controlled write failure")
    });
    cache.remember("agent", State::Working, 1).unwrap();
    let (mutex, wake) = &*cache.shared;
    let mut pending = mutex.lock().unwrap();
    while pending.attempted == 0 {
        pending = wake.wait(pending).unwrap();
    }
    assert_eq!(pending.persisted, 0);
    drop(pending);
    cache.flush().unwrap_err();
    assert_eq!(attempts.load(std::sync::atomic::Ordering::SeqCst), 2);
    assert_eq!(mutex.lock().unwrap().persisted, 0);
    drop(cache);
    assert_eq!(attempts.load(std::sync::atomic::Ordering::SeqCst), 3);
}
