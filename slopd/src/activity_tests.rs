use super::{cache_path, trim_entries, ActivityCache, CacheEntry, MAX_CACHE_ENTRIES};
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

    let restored = ActivityCache::load(path.clone());
    assert_eq!(restored.get("agent"), cache.get("agent"));

    restored.rename("agent", "renamed").unwrap();
    assert!(restored.get("agent").is_none());
    assert!(restored.get("renamed").is_some());
    restored.clear("renamed").unwrap();
    assert!(ActivityCache::load(path.clone()).get("renamed").is_none());
    let _ = std::fs::remove_file(path);
}

#[test]
fn loaded_entries_drop_invalid_rows_duplicates_and_old_overflow() {
    let mut entries = vec![
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
    entries.extend((0..=MAX_CACHE_ENTRIES).map(|n| CacheEntry {
        session: format!("agent-{n}"),
        state: State::Waiting,
        state_since: n as u64,
    }));

    let entries = trim_entries(entries);

    assert_eq!(entries.len(), MAX_CACHE_ENTRIES);
    assert!(entries.iter().all(|entry| {
        !entry.session.trim().is_empty() && entry.session != "down" && entry.session != "duplicate"
    }));
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
    assert!(ActivityCache::load(path.clone()).get("new").is_none());

    let _ = std::fs::remove_file(path);
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

    let _ = std::fs::remove_file(path);
}
