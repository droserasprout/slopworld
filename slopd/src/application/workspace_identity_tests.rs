use super::*;

struct Directory(PathBuf);
impl Directory {
    fn new() -> Self {
        let path = std::env::temp_dir().join(format!("slopd-reservation-{}", uuid::Uuid::new_v4()));
        std::fs::create_dir_all(&path).unwrap();
        Self(path)
    }
}
impl Drop for Directory {
    fn drop(&mut self) {
        drop(std::fs::remove_dir_all(&self.0));
    }
}

#[test]
fn retained_records_caches_and_broken_symlinks_reserve_identity() {
    let root = Directory::new();
    let records = root.0.join("records");
    let caches = root.0.join("caches");
    std::fs::create_dir_all(&records).unwrap();
    std::fs::create_dir_all(&caches).unwrap();
    std::fs::write(records.join("record.toml"), "").unwrap();
    std::fs::create_dir(caches.join("cached")).unwrap();
    std::os::unix::fs::symlink(root.0.join("missing"), records.join("dangling.toml")).unwrap();
    let reservation = Reservation {
        occupied: HashSet::from(["live".into()]),
        records,
        retained: Some(caches),
    };
    for id in ["live", "record", "cached", "dangling"] {
        assert!(reservation.contains(id).unwrap(), "{id}");
    }
    assert!(!reservation.contains("vacant").unwrap());
    assert!(crate::storage_id::valid(&reservation.allocate().unwrap()));
}

#[test]
fn inventory_errors_are_not_available_identities() {
    let root = Directory::new();
    let cycle = root.0.join("cycle");
    std::os::unix::fs::symlink(&cycle, &cycle).unwrap();
    for reservation in [
        Reservation {
            occupied: HashSet::new(),
            records: cycle.clone(),
            retained: None,
        },
        Reservation {
            occupied: HashSet::new(),
            records: root.0.join("absent"),
            retained: Some(cycle),
        },
    ] {
        reservation.contains("candidate").unwrap_err();
        reservation.allocate().unwrap_err();
    }
}

#[test]
fn identity_handles_use_bound_record_roots_and_retry_inventory_failures() {
    let root = Directory::new();
    let config = root.0.join("config");
    let data = root.0.join("data");
    std::fs::create_dir_all(&config).unwrap();
    std::fs::create_dir_all(&data).unwrap();
    let binding = StorageBinding::new(&config, &data, &config.join("config.toml")).unwrap();
    let identities = WorkspaceIdentities::new(&binding);
    let paths = [
        data.join("agents"),
        config.join("projects"),
        data.join("worktrees"),
    ];
    for path in &paths {
        std::os::unix::fs::symlink(path, path).unwrap();
    }
    let cfg = Config::default();
    let worktrees = Store::default();
    identities
        .allocate_agent_identity(HashSet::new())
        .unwrap_err();
    identities
        .allocate_project_identity(&cfg, &worktrees)
        .unwrap_err();
    identities
        .allocate_worktree_identity(HashSet::new())
        .unwrap_err();
    for path in paths {
        std::fs::remove_file(path).unwrap();
    }
    for id in [
        identities.allocate_agent_identity(HashSet::new()).unwrap(),
        identities
            .allocate_project_identity(&cfg, &worktrees)
            .unwrap(),
        identities
            .allocate_worktree_identity(HashSet::new())
            .unwrap(),
    ] {
        assert!(crate::storage_id::valid(&id));
    }
}
