use super::*;
use std::fs;
use std::time::{Duration, SystemTime};

fn session(name: &str) -> SessionCfg {
    SessionCfg {
        name: name.into(),
        state_id: uuid::Uuid::new_v4().to_string(),
        ..Default::default()
    }
}

fn seed(s: &SessionCfg) -> PathBuf {
    let path = state_dir(s).unwrap();
    fs::create_dir_all(path.join("home")).unwrap();
    fs::write(path.join("home/memory"), "remember me").unwrap();
    fs::create_dir_all(persistent_tmp_path(s).unwrap()).unwrap();
    fs::write(path.join("tmp/work"), "unfinished work").unwrap();
    path
}

fn key(path: &Path) -> &str {
    path.file_name().unwrap().to_str().unwrap()
}

fn age(path: &Path, days: u64) {
    fs::File::open(path)
        .unwrap()
        .set_modified(SystemTime::now() - Duration::from_secs(days * 24 * 60 * 60))
        .unwrap();
}

#[test]
fn trash_restore_uses_identity_after_rename_and_can_roll_back() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let mut s = session("original");
    let live = seed(&s);
    let trash = trash_state(&s, "reset / unsafe").unwrap().unwrap();
    assert!(!live.exists());
    assert!(key(&trash).contains("-reset---unsafe-"));
    assert_eq!(trashed_session(key(&trash)).unwrap().name, "original");
    assert_eq!(
        fs::read(trash.join("tmp/work")).unwrap(),
        b"unfinished work"
    );
    s.name = "renamed".into();
    assert_eq!(
        restore_stored_state(key(&trash), &[s.clone()]).unwrap(),
        "renamed"
    );
    assert!(!trash.exists());
    assert_eq!(fs::read(live.join("home/memory")).unwrap(), b"remember me");
    rollback_restored_state(key(&trash), &s).unwrap();
    assert!(!live.exists());
    assert!(trash.join(TRASH_SESSION).is_file());
    restore_trashed_state(&s, &trash).unwrap();
    assert_eq!(fs::read(live.join("tmp/work")).unwrap(), b"unfinished work");
    assert!(!live.join(TRASH_SESSION).exists());
    finish_restored_state(&s).unwrap();
    restore_trashed_state(&s, &trash).unwrap();
}

#[test]
fn restore_deleted_agent_retains_archived_identity_and_rejects_fresh_state() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let s = session("deleted");
    let live = seed(&s);
    let trash = trash_state(&s, "delete").unwrap().unwrap();
    seed(&s);
    fs::write(live.join("home/memory"), "new state").unwrap();
    assert!(
        restore_stored_state(key(&trash), &[])
            .unwrap_err()
            .to_string()
            .contains("fresh state")
    );
    assert_eq!(fs::read(live.join("home/memory")).unwrap(), b"new state");
    assert_eq!(fs::read(trash.join("home/memory")).unwrap(), b"remember me");
    remove_ephemeral_state(&s).unwrap();
    assert_eq!(restore_stored_state(key(&trash), &[]).unwrap(), "deleted");
    finish_restored_state(&s).unwrap();
    assert_eq!(fs::read(live.join("home/memory")).unwrap(), b"remember me");
}

#[test]
fn restore_rejects_missing_corrupt_and_unsafe_metadata_without_moving_data() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    assert!(
        restore_stored_state("missing", &[])
            .unwrap_err()
            .to_string()
            .contains("no such")
    );
    let trash = trash_root().join("broken");
    fs::create_dir_all(&trash).unwrap();
    fs::write(trash.join("payload"), "keep").unwrap();
    restore_stored_state("broken", &[]).unwrap_err();
    fs::write(trash.join(TRASH_SESSION), "invalid [toml").unwrap();
    restore_stored_state("broken", &[]).unwrap_err();
    for identity in ["../escape", ".trash", "not-a-uuid"] {
        let mut s = session("broken");
        s.state_id = identity.into();
        fs::write(trash.join(TRASH_SESSION), toml::to_string(&s).unwrap()).unwrap();
        assert!(
            restore_stored_state("broken", &[])
                .unwrap_err()
                .to_string()
                .contains("invalid private-state identity")
        );
        assert_eq!(fs::read(trash.join("payload")).unwrap(), b"keep");
    }
}

#[test]
fn failed_trash_metadata_write_restores_the_original_tree() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let s = session("rollback");
    let live = seed(&s);
    fs::create_dir(live.join(TRASH_SESSION)).unwrap();
    trash_state(&s, "reset").unwrap_err();
    assert_eq!(fs::read(live.join("home/memory")).unwrap(), b"remember me");
    assert_eq!(fs::read_dir(trash_root()).unwrap().count(), 0);
}

#[test]
fn quick_inventory_keeps_paths_and_ownership_without_tree_sizes() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let active = session("active");
    seed(&active);
    let orphan = session("orphan");
    seed(&orphan);
    let deleted = session("deleted");
    seed(&deleted);
    let expired = trash_state(&deleted, "delete").unwrap().unwrap();
    age(&expired, 15);
    let quick = stored_states(std::slice::from_ref(&active), false);
    let measured = stored_states(&[active], true);
    assert!(expired.exists(), "inventory must not purge expired trash");
    assert_eq!(quick.len(), 3);
    assert_eq!(quick.len(), measured.len());
    for (quick, measured) in quick.iter().zip(&measured) {
        assert_eq!(quick.kind, measured.kind);
        assert_eq!(quick.key, measured.key);
        assert_eq!(quick.path, measured.path);
        assert_eq!(quick.session, measured.session);
        assert_eq!(quick.modified, measured.modified);
        assert_eq!(quick.bytes, 0);
        assert!(measured.bytes > 0);
    }
}

#[test]
fn inventory_distinguishes_owners_orphans_and_archived_sessions() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    assert!(stored_states(&[], true).is_empty());
    let active = session("active");
    seed(&active);
    let orphan = session("orphan");
    seed(&orphan);
    let deleted = session("deleted");
    seed(&deleted);
    let deleted_trash = trash_state(&deleted, "delete").unwrap().unwrap();
    let mut renamed = session("before");
    seed(&renamed);
    let renamed_trash = trash_state(&renamed, "reset").unwrap().unwrap();
    renamed.name = "after".into();
    let entries = stored_states(&[active.clone(), renamed], true);
    assert_eq!(entries.len(), 4);
    assert_eq!(
        entries.iter().map(|e| e.kind.as_str()).collect::<Vec<_>>(),
        ["active", "orphan", "trash", "trash"]
    );
    let active_entry = &entries[0];
    assert_eq!(active_entry.session.as_deref(), Some("active"));
    assert_eq!(active_entry.key, active.state_id);
    assert_eq!(
        active_entry.path,
        state_dir(&active).unwrap().to_str().unwrap()
    );
    assert_eq!(active_entry.bytes, 26);
    assert!(active_entry.modified > 0);
    assert_eq!(entries[1].key, orphan.state_id);
    assert!(entries[1].session.is_none());
    for (path, name) in [(deleted_trash, "deleted"), (renamed_trash, "after")] {
        assert_eq!(
            entries
                .iter()
                .find(|e| e.key == key(&path))
                .unwrap()
                .session
                .as_deref(),
            Some(name)
        );
    }
}

#[test]
fn deletion_rejects_owned_state_and_traversal_and_removes_only_requested_orphans() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let owned = session("owned");
    let live = seed(&owned);
    let sessions = [owned.clone()];
    assert!(
        delete_stored_state("orphan", &owned.state_id, &sessions)
            .unwrap_err()
            .to_string()
            .contains("still owned")
    );
    for kind in ["active", "unknown", ""] {
        assert!(delete_stored_state(kind, &owned.state_id, &sessions).is_err());
    }
    for bad in ["", ".", "..", "../escape", "/", ".trash", "one/two"] {
        for kind in ["orphan", "trash"] {
            assert!(delete_stored_state(kind, bad, &sessions).is_err());
        }
        restore_stored_state(bad, &sessions).unwrap_err();
    }
    assert_eq!(fs::read(live.join("home/memory")).unwrap(), b"remember me");
    let orphan = session("orphan");
    let orphan_path = seed(&orphan);
    delete_stored_state("orphan", &orphan.state_id, &sessions).unwrap();
    assert!(!orphan_path.exists());
    assert!(live.exists());
    assert!(delete_stored_state("orphan", "missing", &sessions).is_err());
}

#[test]
fn purge_removes_only_old_trash_and_empty_trash_preserves_its_root() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    assert_eq!(empty_trash().unwrap(), 0);
    assert_eq!(purge_trash().unwrap(), 0);
    let dormant = session("dormant");
    let live = seed(&dormant);
    age(&live, 30);
    let old = session("old");
    seed(&old);
    let old_trash = trash_state(&old, "reset").unwrap().unwrap();
    age(&old_trash, 15);
    let recent = session("recent");
    seed(&recent);
    let recent_trash = trash_state(&recent, "reset").unwrap().unwrap();
    let old_file = trash_root().join("old-file");
    fs::write(&old_file, "old").unwrap();
    age(&old_file, 15);
    let future = trash_root().join("future");
    fs::write(&future, "future").unwrap();
    fs::File::open(&future)
        .unwrap()
        .set_modified(SystemTime::now() + Duration::from_secs(3600))
        .unwrap();
    assert_eq!(purge_trash().unwrap(), 2);
    assert!(!old_trash.exists() && !old_file.exists());
    assert!(recent_trash.exists() && future.exists() && live.exists());
    assert_eq!(empty_trash().unwrap(), 2);
    assert!(trash_root().is_dir() && live.is_dir());
    assert_eq!(empty_trash().unwrap(), 0);
}

#[cfg(unix)]
#[test]
fn inventory_and_deletion_do_not_follow_symlinks() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let outside = root.join("outside");
    fs::create_dir_all(&outside).unwrap();
    fs::write(outside.join("keep"), "external data").unwrap();
    fs::create_dir_all(trash_root()).unwrap();
    for (kind, base) in [("orphan", state_root()), ("trash", trash_root())] {
        for (name, target) in [
            ("link", outside.clone()),
            ("dangling", root.join("missing")),
        ] {
            let path = base.join(name);
            std::os::unix::fs::symlink(&target, &path).unwrap();
            assert_eq!(tree_size(&path), fs::symlink_metadata(&path).unwrap().len());
            let entries = stored_states(&[], true);
            let row = entries
                .iter()
                .find(|row| row.kind == kind && row.key == name)
                .unwrap();
            assert_eq!(row.bytes, fs::symlink_metadata(&path).unwrap().len());
            assert!(row.session.is_none());
            delete_stored_state(kind, name, &[]).unwrap();
            fs::symlink_metadata(&path).unwrap_err();
            assert_eq!(fs::read(outside.join("keep")).unwrap(), b"external data");
        }
    }
    std::os::unix::fs::symlink(&outside, trash_root().join("link")).unwrap();
    assert_eq!(empty_trash().unwrap(), 1);
    assert_eq!(fs::read(outside.join("keep")).unwrap(), b"external data");
    assert_eq!(tree_size(&root.join("missing")), 0);
}

#[test]
fn missing_and_ephemeral_state_cleanup_is_idempotent() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let s = session("ephemeral");
    assert!(trash_state(&s, "reset").unwrap().is_none());
    remove_ephemeral_state(&s).unwrap();
    let live = seed(&s);
    remove_ephemeral_state(&s).unwrap();
    remove_ephemeral_state(&s).unwrap();
    assert!(!live.exists());
}

#[test]
fn trash_inventory_ignores_linked_metadata_and_refuses_linked_root() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let s = session("external owner");
    let external = root.join("external");
    fs::create_dir_all(&external).unwrap();
    let metadata = external.join(TRASH_SESSION);
    fs::write(&metadata, toml::to_string(&s).unwrap()).unwrap();
    fs::create_dir_all(trash_root().join("item")).unwrap();
    std::os::unix::fs::symlink(&metadata, trash_root().join("item").join(TRASH_SESSION)).unwrap();
    std::os::unix::fs::symlink(&external, trash_root().join("link")).unwrap();
    let entries = stored_states(&[], true);
    assert_eq!(entries.len(), 2);
    assert!(entries.iter().all(|row| row.session.is_none()));
    assert_eq!(
        entries.iter().find(|row| row.key == "link").unwrap().bytes,
        fs::symlink_metadata(trash_root().join("link"))
            .unwrap()
            .len()
    );
    fs::remove_dir_all(trash_root()).unwrap();
    std::os::unix::fs::symlink(&external, trash_root()).unwrap();
    purge_trash().unwrap_err();
    empty_trash().unwrap_err();
    assert!(stored_states(&[], true).is_empty());
    assert!(metadata.is_file());
}

#[test]
fn trash_cleanup_propagates_non_missing_root_errors() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    fs::create_dir_all(state_root()).unwrap();
    fs::write(trash_root(), "not a directory").unwrap();
    purge_trash().unwrap_err();
}

#[test]
fn identity_inventory_includes_retained_metadata_without_walking_private_contents() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let active = session("active");
    let archived = session("archived");
    seed(&active);
    seed(&archived);
    let trash = trash_state(&archived, "archived").unwrap().unwrap();
    // The metadata is authoritative even after an offline operator renames its key.
    let renamed = trash.with_file_name("operator-renamed-entry");
    fs::rename(trash, &renamed).unwrap();
    std::os::unix::fs::symlink(
        "/unreadable-private-target",
        state_dir(&active).unwrap().join("private-link"),
    )
    .unwrap();
    let ids = retained_state_identities().unwrap();
    assert!(ids.contains(&active.state_id));
    assert!(ids.contains(&archived.state_id));
    fs::write(renamed.join(TRASH_SESSION), "invalid [").unwrap();
    retained_state_identities().unwrap_err();
}
