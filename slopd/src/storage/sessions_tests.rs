use super::super::transaction;
use super::*;
use std::sync::{Arc, Mutex};

struct Fixture {
    root: std::path::PathBuf,
    binding: StorageBinding,
    gate: Arc<tokio::sync::Mutex<()>>,
    store: Arc<Mutex<Store>>,
}

impl Fixture {
    fn new(kind: Kind) -> Self {
        let root =
            std::env::temp_dir().join(format!("slopd-session-records-{}", uuid::Uuid::new_v4()));
        let binding = StorageBinding::new(
            &root.join("config"),
            &root.join("data"),
            &root.join("config/config.toml"),
        )
        .unwrap();
        Self {
            root,
            binding,
            gate: Arc::new(tokio::sync::Mutex::new(())),
            store: Arc::new(Mutex::new(Store::empty(kind))),
        }
    }
    async fn change(&self, prepare: impl FnOnce(&Store) -> Result<Prepared>) -> Result<()> {
        let gate = self.gate.clone().lock_owned().await;
        let prepared = prepare(&self.store.lock().unwrap())?;
        let store = self.store.clone();
        transaction::commit(
            self.binding.clone(),
            vec![prepared.change.clone()],
            gate,
            move || async move {
                store.lock().unwrap().publish(prepared);
            },
        )
        .await
    }
    fn path(&self, kind: Kind, id: &str) -> std::path::PathBuf {
        kind.target(id).resolve(&self.binding).unwrap()
    }
    async fn reload(&self, kind: Kind) -> Result<()> {
        let store = Store::load(&self.binding, kind).await?;
        *self.store.lock().unwrap() = store;
        Ok(())
    }
    fn value(&self, id: &str) -> Definition {
        self.store.lock().unwrap().get(id).unwrap().clone()
    }
}
impl Drop for Fixture {
    fn drop(&mut self) {
        drop(std::fs::remove_dir_all(&self.root));
    }
}

const FIRST: &str = "ffffffffffffffff";
const SECOND: &str = "0000000000000000";
fn agent(id: &str, name: &str) -> Definition {
    Definition::Agent(Box::new(SessionCfg {
        state_id: id.into(),
        name: name.into(),
        ..Default::default()
    }))
}
fn shell(id: &str, name: &str) -> Definition {
    Definition::HostShell(HostTerminalCfg {
        id: id.into(),
        name: name.into(),
        ..Default::default()
    })
}

#[tokio::test]
async fn startup_preserves_insertion_order_instead_of_random_filename_order() {
    for kind in [Kind::Agent, Kind::HostShell] {
        let f = Fixture::new(kind);
        let create = match kind {
            Kind::Agent => agent,
            Kind::HostShell => shell,
        };
        f.change(|store| store.create(create(FIRST, "z-first")))
            .await
            .unwrap();
        f.change(|store| store.create(create(SECOND, "a-second")))
            .await
            .unwrap();
        f.reload(kind).await.unwrap();
        let names: Vec<_> = f
            .store
            .lock()
            .unwrap()
            .ordered()
            .iter()
            .map(|row| row.name().to_owned())
            .collect();
        assert_eq!(names, ["z-first", "a-second"]);
    }
}

#[tokio::test]
async fn edits_clear_known_fields_keep_extensions_and_leave_siblings_untouched() {
    let f = Fixture::new(Kind::Agent);
    f.change(|s| s.create(agent(FIRST, "agent"))).await.unwrap();
    f.change(|s| s.create(agent(SECOND, "sibling")))
        .await
        .unwrap();
    let path = f.path(Kind::Agent, FIRST);
    let mut raw: toml::Value =
        toml::from_str(&tokio::fs::read_to_string(&path).await.unwrap()).unwrap();
    let table = raw.as_table_mut().unwrap();
    table.insert("label".into(), "old label".into());
    table.insert("persistent_tmp".into(), false.into());
    table.insert("extension".into(), "keep".into());
    table.insert("worker_token".into(), "never-persist".into());
    table.insert("reader_key".into(), "never-persist".into());
    raw["dns"]
        .as_table_mut()
        .unwrap()
        .insert("future".into(), "keep-dns".into());
    tokio::fs::write(&path, toml::to_string(&raw).unwrap())
        .await
        .unwrap();
    f.reload(Kind::Agent).await.unwrap();
    let sibling = f.path(Kind::Agent, SECOND);
    let bytes = tokio::fs::read(&sibling).await.unwrap();
    let stamp = tokio::fs::metadata(&sibling)
        .await
        .unwrap()
        .modified()
        .unwrap();
    let Definition::Agent(mut updated) = f.value(FIRST) else {
        panic!()
    };
    updated.label = None;
    updated.name = "renamed".into();
    f.change(|s| s.update(FIRST, Definition::Agent(updated)))
        .await
        .unwrap();
    let raw: toml::Value =
        toml::from_str(&tokio::fs::read_to_string(&path).await.unwrap()).unwrap();
    for removed in ["label", "persistent_tmp", "worker_token", "reader_key"] {
        assert!(raw.get(removed).is_none(), "{removed}");
    }
    assert_eq!(raw["extension"].as_str(), Some("keep"));
    assert_eq!(raw["dns"]["future"].as_str(), Some("keep-dns"));
    assert_eq!(raw["state_id"].as_str(), Some(FIRST));
    assert_eq!(raw["storage_order"].as_integer(), Some(0));
    assert_eq!(tokio::fs::read(&sibling).await.unwrap(), bytes);
    assert_eq!(
        tokio::fs::metadata(sibling)
            .await
            .unwrap()
            .modified()
            .unwrap(),
        stamp
    );
    f.reload(Kind::Agent).await.unwrap();
    assert_eq!(f.value(FIRST).name(), "renamed");
}

#[tokio::test]
async fn captured_snapshots_worker_linkage_and_startup_fields_survive_edit() {
    let f = Fixture::new(Kind::Agent);
    let Definition::Agent(mut value) = agent(FIRST, "worker") else {
        panic!()
    };
    value.worker = true;
    value.parent = "parent".into();
    value.task_id = "legacy-task".into();
    value.worktree = "tree".into();
    value.autostart = true;
    value.auto_resume = true;
    value.worker_token = Some("secret".into());
    value.command_snapshot = Some(crate::presets::CommandPreset {
        name: "tool".into(),
        cmd: "tool --fixed".into(),
        ..Default::default()
    });
    value.sandbox_snapshots.push(crate::presets::SandboxPreset {
        name: "policy".into(),
        ro: vec!["/fixture".into()],
        ..Default::default()
    });
    f.change(|s| s.create(Definition::Agent(value)))
        .await
        .unwrap();
    let path = f.path(Kind::Agent, FIRST);
    let mut raw: toml::Value =
        toml::from_str(&tokio::fs::read_to_string(&path).await.unwrap()).unwrap();
    assert!(raw.get("worker_token").is_none());
    raw["command_snapshot"]
        .as_table_mut()
        .unwrap()
        .insert("future".into(), "command-extension".into());
    raw["sandbox_snapshots"][0]
        .as_table_mut()
        .unwrap()
        .insert("future".into(), "sandbox-extension".into());
    tokio::fs::write(&path, toml::to_string(&raw).unwrap())
        .await
        .unwrap();
    f.reload(Kind::Agent).await.unwrap();
    let Definition::Agent(mut value) = f.value(FIRST) else {
        panic!()
    };
    value.label = Some("label".into());
    f.change(|s| s.update(FIRST, Definition::Agent(value)))
        .await
        .unwrap();
    f.reload(Kind::Agent).await.unwrap();
    let Definition::Agent(value) = f.value(FIRST) else {
        panic!()
    };
    assert!(value.worker && value.autostart && value.auto_resume);
    assert_eq!(
        (&*value.parent, &*value.task_id, &*value.worktree),
        ("parent", "legacy-task", "tree")
    );
    assert_eq!(value.command_snapshot.unwrap().cmd, "tool --fixed");
    assert_eq!(value.sandbox_snapshots[0].ro, ["/fixture"]);
    let raw: toml::Value = toml::from_str(&tokio::fs::read_to_string(path).await.unwrap()).unwrap();
    assert_eq!(
        raw["command_snapshot"]["future"].as_str(),
        Some("command-extension")
    );
    assert_eq!(
        raw["sandbox_snapshots"][0]["future"].as_str(),
        Some("sandbox-extension")
    );
}

#[tokio::test]
async fn host_directory_updates_keep_identity_order_and_other_files() {
    let f = Fixture::new(Kind::HostShell);
    f.change(|s| s.create(shell(FIRST, "shell"))).await.unwrap();
    let sentinel = f.binding.settings.clone();
    crate::paths::write_atomic_async(&sentinel, "unchanged settings", Some(0o600))
        .await
        .unwrap();
    let Definition::HostShell(mut value) = f.value(FIRST) else {
        panic!()
    };
    value.path = "/changed".into();
    value.label = Some("label".into());
    value.autostart = false;
    f.change(|s| s.update(FIRST, Definition::HostShell(value)))
        .await
        .unwrap();
    f.reload(Kind::HostShell).await.unwrap();
    let Definition::HostShell(value) = f.value(FIRST) else {
        panic!()
    };
    assert_eq!(value.path, "/changed");
    assert_eq!(value.label.as_deref(), Some("label"));
    assert!(!value.autostart);
    assert_eq!(
        tokio::fs::read_to_string(sentinel).await.unwrap(),
        "unchanged settings"
    );
}

#[tokio::test]
async fn retirement_is_repeatable_and_late_updates_cannot_recreate_or_touch_reused_names() {
    let f = Fixture::new(Kind::Agent);
    f.change(|s| s.create(agent(FIRST, "same-name")))
        .await
        .unwrap();
    let stale = f.value(FIRST);
    f.change(|s| Ok(s.retire(FIRST).unwrap())).await.unwrap();
    assert!(f.store.lock().unwrap().retire(FIRST).is_none());
    f.change(|s| s.create(agent(SECOND, "same-name")))
        .await
        .unwrap();
    assert!(f.change(|s| s.update(FIRST, stale)).await.is_err());
    assert!(!f.path(Kind::Agent, FIRST).exists());
    assert_eq!(f.value(SECOND).name(), "same-name");
}

#[tokio::test]
async fn create_collision_and_write_failure_do_not_publish_and_allow_retry() {
    let f = Fixture::new(Kind::HostShell);
    let path = f.path(Kind::HostShell, FIRST);
    crate::paths::write_atomic_async(&path, "occupied", Some(0o600))
        .await
        .unwrap();
    assert!(f.change(|s| s.create(shell(FIRST, "shell"))).await.is_err());
    assert!(f.store.lock().unwrap().get(FIRST).is_none());
    assert_eq!(tokio::fs::read_to_string(&path).await.unwrap(), "occupied");
    tokio::fs::remove_file(&path).await.unwrap();
    f.change(|s| s.create(shell(FIRST, "shell"))).await.unwrap();
    tokio::fs::remove_file(&path).await.unwrap();
    tokio::fs::create_dir(&path).await.unwrap();
    let Definition::HostShell(mut next) = f.value(FIRST) else {
        panic!()
    };
    next.path = "/next".into();
    assert!(
        f.change(|s| s.update(FIRST, Definition::HostShell(next.clone())))
            .await
            .is_err()
    );
    let Definition::HostShell(old) = f.value(FIRST) else {
        panic!()
    };
    assert_eq!(old.path, "");
    tokio::fs::remove_dir(&path).await.unwrap();
    f.change(|s| s.update(FIRST, Definition::HostShell(next)))
        .await
        .unwrap();
}

#[tokio::test]
async fn startup_rejects_bad_identity_duplicate_names_orders_and_malformed_records() {
    let f = Fixture::new(Kind::HostShell);
    assert!(
        Store::load(&f.binding, Kind::HostShell)
            .await
            .unwrap()
            .ordered()
            .is_empty()
    );
    f.change(|s| s.create(shell(FIRST, "one"))).await.unwrap();
    let path = f.path(Kind::HostShell, SECOND);
    for text in [
        format!("id = '{FIRST}'\nname = 'two'\nstorage_order = 1"),
        format!("id = '{SECOND}'\nname = 'one'\nstorage_order = 1"),
        format!("id = '{SECOND}'\nname = 'two'\nstorage_order = 0"),
        format!("id = '{SECOND}'\nname = 'two'"),
        format!("id = '{SECOND}'\nname = 'two'\nstorage_order = -1"),
        "invalid [".into(),
    ] {
        tokio::fs::write(&path, &text).await.unwrap();
        assert!(
            Store::load(&f.binding, Kind::HostShell).await.is_err(),
            "{text}"
        );
        assert_eq!(tokio::fs::read_to_string(&path).await.unwrap(), text);
    }
}

#[tokio::test]
async fn opaque_identity_is_retained_and_readers_are_not_records() {
    let f = Fixture::new(Kind::Agent);
    let id = crate::storage_id::draft_identity();
    f.change(|s| s.create(agent(&id, "legacy"))).await.unwrap();
    f.reload(Kind::Agent).await.unwrap();
    assert_eq!(f.value(&id).id(), id);
    let Definition::Agent(mut reader) = agent(FIRST, "reader") else {
        panic!()
    };
    reader.intent = "reader".into();
    assert!(
        f.change(|s| s.create(Definition::Agent(reader)))
            .await
            .is_err()
    );
    assert!(!f.path(Kind::Agent, FIRST).exists());
}

#[tokio::test]
async fn cleared_limits_keep_extensions_and_removed_snapshots_stay_removed() {
    let f = Fixture::new(Kind::Agent);
    let Definition::Agent(mut value) = agent(FIRST, "agent") else {
        panic!()
    };
    value.limits.memory_mb = Some(256);
    value.command_snapshot = Some(crate::presets::CommandPreset {
        name: "tool".into(),
        cmd: "tool".into(),
        ..Default::default()
    });
    f.change(|s| s.create(Definition::Agent(value)))
        .await
        .unwrap();
    let path = f.path(Kind::Agent, FIRST);
    let mut raw: toml::Value =
        toml::from_str(&tokio::fs::read_to_string(&path).await.unwrap()).unwrap();
    raw["limits"]
        .as_table_mut()
        .unwrap()
        .insert("future".into(), "keep".into());
    tokio::fs::write(&path, toml::to_string(&raw).unwrap())
        .await
        .unwrap();
    f.reload(Kind::Agent).await.unwrap();
    let Definition::Agent(mut value) = f.value(FIRST) else {
        panic!()
    };
    value.limits = Default::default();
    value.command_snapshot = None;
    f.change(|s| s.update(FIRST, Definition::Agent(value)))
        .await
        .unwrap();
    let raw: toml::Value =
        toml::from_str(&tokio::fs::read_to_string(&path).await.unwrap()).unwrap();
    assert_eq!(raw["limits"]["future"].as_str(), Some("keep"));
    assert!(raw["limits"].get("memory_mb").is_none());
    assert!(raw.get("command_snapshot").is_none());
    f.reload(Kind::Agent).await.unwrap();
}

#[tokio::test]
async fn aliases_and_invalid_filenames_fail_without_reading_external_records() {
    use std::os::unix::fs::symlink;
    let f = Fixture::new(Kind::Agent);
    let outside = f.root.join("outside");
    tokio::fs::create_dir_all(&outside).await.unwrap();
    tokio::fs::create_dir_all(&f.binding.data).await.unwrap();
    let directory = f.binding.data.join("agents");
    symlink(&outside, &directory).unwrap();
    assert!(Store::load(&f.binding, Kind::Agent).await.is_err());
    tokio::fs::remove_file(&directory).await.unwrap();
    tokio::fs::create_dir(&directory).await.unwrap();
    let invalid = directory.join("not-an-id.toml");
    tokio::fs::write(&invalid, "invalid").await.unwrap();
    assert!(Store::load(&f.binding, Kind::Agent).await.is_err());
    tokio::fs::remove_file(invalid).await.unwrap();
    let target = outside.join("agent.toml");
    tokio::fs::write(&target, "outside bytes").await.unwrap();
    symlink(&target, directory.join(format!("{FIRST}.toml"))).unwrap();
    assert!(Store::load(&f.binding, Kind::Agent).await.is_err());
    assert_eq!(
        tokio::fs::read_to_string(target).await.unwrap(),
        "outside bytes"
    );
}

#[tokio::test]
async fn cancelled_request_cannot_skip_record_index_publication_after_commit() {
    let f = Fixture::new(Kind::HostShell);
    let prepared = f
        .store
        .lock()
        .unwrap()
        .create(shell(FIRST, "shell"))
        .unwrap();
    let binding = f.binding.clone();
    let gate = f.gate.clone().lock_owned().await;
    let committed = Arc::new(tokio::sync::Barrier::new(2));
    let release = Arc::new(tokio::sync::Notify::new());
    let store = f.store.clone();
    let entered = committed.clone();
    let resume = release.clone();
    let request = tokio::spawn(async move {
        transaction::commit(
            binding,
            vec![prepared.change.clone()],
            gate,
            move || async move {
                entered.wait().await;
                resume.notified().await;
                store.lock().unwrap().publish(prepared);
            },
        )
        .await
    });
    committed.wait().await;
    assert!(f.path(Kind::HostShell, FIRST).exists());
    assert!(f.store.lock().unwrap().get(FIRST).is_none());
    request.abort();
    drop(request.await);
    f.gate.try_lock().unwrap_err();
    release.notify_one();
    let _guard = tokio::time::timeout(std::time::Duration::from_secs(5), f.gate.lock())
        .await
        .unwrap();
    assert_eq!(f.value(FIRST).name(), "shell");
}
