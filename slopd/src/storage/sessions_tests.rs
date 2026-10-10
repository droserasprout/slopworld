use super::super::{transaction, workspace::Store};
use super::*;
use std::sync::Arc;

struct Fixture<T: Record> {
    root: std::path::PathBuf,
    binding: StorageBinding,
    gate: Arc<tokio::sync::Mutex<()>>,
    store: Store<T>,
}

impl<T: Record> Fixture<T> {
    fn new() -> Self {
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
            store: Store::empty(),
        }
    }
    async fn change(&mut self, edit: impl FnOnce(&mut Vec<T>)) -> Result<()> {
        let gate = self.gate.clone().lock_owned().await;
        let mut values = self.store.ordered();
        edit(&mut values);
        let plan = self.store.prepare(values)?;
        transaction::commit_in_operation(&self.binding, plan.changes.clone(), &gate).await?;
        self.store.publish(plan);
        Ok(())
    }
    fn path(&self, id: &str) -> std::path::PathBuf {
        T::target(id).resolve(&self.binding).unwrap()
    }
    async fn reload(&mut self) -> Result<()> {
        self.store = Store::load(&self.binding).await?;
        Ok(())
    }
    fn value(&self, id: &str) -> T {
        self.store.get(id).unwrap().clone()
    }
}
impl<T: Record> Drop for Fixture<T> {
    fn drop(&mut self) {
        drop(std::fs::remove_dir_all(&self.root));
    }
}

const FIRST: &str = "ffffffffffffffff";
const SECOND: &str = "0000000000000000";
fn agent(id: &str, name: &str) -> SessionCfg {
    SessionCfg {
        state_id: id.into(),
        name: name.into(),
        ..Default::default()
    }
}
fn shell(id: &str, name: &str) -> HostTerminalCfg {
    HostTerminalCfg {
        id: id.into(),
        name: name.into(),
        ..Default::default()
    }
}

#[tokio::test]
async fn startup_preserves_insertion_order_instead_of_random_filename_order() {
    async fn check<T: Record>(create: fn(&str, &str) -> T) {
        let mut f = Fixture::new();
        f.change(|rows| rows.push(create(FIRST, "z-first")))
            .await
            .unwrap();
        f.change(|rows| rows.push(create(SECOND, "a-second")))
            .await
            .unwrap();
        f.reload().await.unwrap();
        assert_eq!(
            f.store.ordered().iter().map(Record::id).collect::<Vec<_>>(),
            [FIRST, SECOND]
        );
    }
    check(agent).await;
    check(shell).await;
}

#[tokio::test]
async fn edits_clear_known_fields_keep_extensions_and_leave_siblings_untouched() {
    let mut f = Fixture::<SessionCfg>::new();
    f.change(|s| s.push(agent(FIRST, "agent"))).await.unwrap();
    f.change(|s| s.push(agent(SECOND, "sibling")))
        .await
        .unwrap();
    let path = f.path(FIRST);
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
    f.reload().await.unwrap();
    let sibling = f.path(SECOND);
    let bytes = tokio::fs::read(&sibling).await.unwrap();
    let stamp = tokio::fs::metadata(&sibling)
        .await
        .unwrap()
        .modified()
        .unwrap();
    let mut updated = f.value(FIRST);
    updated.label = None;
    updated.name = "renamed".into();
    f.change(|s| s[0] = updated).await.unwrap();
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
    f.reload().await.unwrap();
    assert_eq!(f.value(FIRST).name, "renamed");
}

#[tokio::test]
async fn captured_snapshots_worker_linkage_and_startup_fields_survive_edit() {
    let mut f = Fixture::<SessionCfg>::new();
    let mut value = agent(FIRST, "worker");
    value.worker = true;
    value.parent = "parent".into();
    value.task_id = "assigned-task".into();
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
    f.change(|s| s.push(value)).await.unwrap();
    let path = f.path(FIRST);
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
    f.reload().await.unwrap();
    let mut value = f.value(FIRST);
    value.label = Some("label".into());
    f.change(|s| s[0] = value).await.unwrap();
    f.reload().await.unwrap();
    let value = f.value(FIRST);
    assert!(value.worker && value.autostart && value.auto_resume);
    assert_eq!(
        (&*value.parent, &*value.task_id, &*value.worktree),
        ("parent", "assigned-task", "tree")
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
    let mut f = Fixture::<HostTerminalCfg>::new();
    f.change(|s| s.push(shell(FIRST, "shell"))).await.unwrap();
    let sentinel = f.binding.settings.clone();
    crate::paths::write_atomic_async(&sentinel, "unchanged settings", Some(0o600))
        .await
        .unwrap();
    let mut value = f.value(FIRST);
    value.path = "/changed".into();
    value.label = Some("label".into());
    value.autostart = false;
    f.change(|s| s[0] = value).await.unwrap();
    f.reload().await.unwrap();
    let value = f.value(FIRST);
    assert_eq!(value.path, "/changed");
    assert_eq!(value.label.as_deref(), Some("label"));
    assert!(!value.autostart);
    assert_eq!(
        tokio::fs::read_to_string(sentinel).await.unwrap(),
        "unchanged settings"
    );
}

#[tokio::test]
async fn retirement_is_repeatable_and_reused_names_keep_distinct_identities() {
    let mut f = Fixture::<SessionCfg>::new();
    f.change(|rows| rows.push(agent(FIRST, "same-name")))
        .await
        .unwrap();
    f.change(Vec::clear).await.unwrap();
    assert!(f.store.prepare(vec![]).unwrap().changes.is_empty());
    f.change(|rows| rows.push(agent(SECOND, "same-name")))
        .await
        .unwrap();
    assert!(!f.path(FIRST).exists());
    assert_eq!(f.value(SECOND).name, "same-name");
}

#[tokio::test]
async fn create_collision_and_write_failure_do_not_publish_and_allow_retry() {
    let mut f = Fixture::<HostTerminalCfg>::new();
    let path = f.path(FIRST);
    crate::paths::write_atomic_async(&path, "occupied", Some(0o600))
        .await
        .unwrap();
    assert!(f.change(|s| s.push(shell(FIRST, "shell"))).await.is_err());
    assert!(f.store.get(FIRST).is_none());
    assert_eq!(tokio::fs::read_to_string(&path).await.unwrap(), "occupied");
    tokio::fs::remove_file(&path).await.unwrap();
    f.change(|s| s.push(shell(FIRST, "shell"))).await.unwrap();
    tokio::fs::remove_file(&path).await.unwrap();
    tokio::fs::create_dir(&path).await.unwrap();
    let mut next = f.value(FIRST);
    next.path = "/next".into();
    assert!(f.change(|s| s[0] = next.clone()).await.is_err());
    let old = f.value(FIRST);
    assert_eq!(old.path, "");
    tokio::fs::remove_dir(&path).await.unwrap();
    f.change(|s| s[0] = next).await.unwrap();
}

#[tokio::test]
async fn startup_rejects_bad_identity_duplicate_names_orders_and_malformed_records() {
    let mut f = Fixture::<HostTerminalCfg>::new();
    assert!(
        Store::<HostTerminalCfg>::load(&f.binding)
            .await
            .unwrap()
            .ordered()
            .is_empty()
    );
    f.change(|s| s.push(shell(FIRST, "one"))).await.unwrap();
    let path = f.path(SECOND);
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
            Store::<HostTerminalCfg>::load(&f.binding).await.is_err(),
            "{text}"
        );
        assert_eq!(tokio::fs::read_to_string(&path).await.unwrap(), text);
    }
}

#[tokio::test]
async fn opaque_identity_is_retained_and_readers_are_not_records() {
    let mut f = Fixture::<SessionCfg>::new();
    let id = crate::storage_id::draft_identity();
    f.change(|s| s.push(agent(&id, "worker"))).await.unwrap();
    f.reload().await.unwrap();
    assert_eq!(f.value(&id).id(), id);
    let mut reader = agent(FIRST, "reader");
    reader.intent = "reader".into();
    assert!(f.change(|s| s.push(reader)).await.is_err());
    assert!(!f.path(FIRST).exists());
}

#[tokio::test]
async fn cleared_limits_keep_extensions_and_removed_snapshots_stay_removed() {
    let mut f = Fixture::<SessionCfg>::new();
    let mut value = agent(FIRST, "agent");
    value.limits.memory_mb = Some(256);
    value.command_snapshot = Some(crate::presets::CommandPreset {
        name: "tool".into(),
        cmd: "tool".into(),
        ..Default::default()
    });
    f.change(|s| s.push(value)).await.unwrap();
    let path = f.path(FIRST);
    let mut raw: toml::Value =
        toml::from_str(&tokio::fs::read_to_string(&path).await.unwrap()).unwrap();
    raw["limits"]
        .as_table_mut()
        .unwrap()
        .insert("future".into(), "keep".into());
    tokio::fs::write(&path, toml::to_string(&raw).unwrap())
        .await
        .unwrap();
    f.reload().await.unwrap();
    let mut value = f.value(FIRST);
    value.limits = Default::default();
    value.command_snapshot = None;
    f.change(|s| s[0] = value).await.unwrap();
    let raw: toml::Value =
        toml::from_str(&tokio::fs::read_to_string(&path).await.unwrap()).unwrap();
    assert_eq!(raw["limits"]["future"].as_str(), Some("keep"));
    assert!(raw["limits"].get("memory_mb").is_none());
    assert!(raw.get("command_snapshot").is_none());
    f.reload().await.unwrap();
}

#[tokio::test]
async fn aliases_and_invalid_filenames_fail_without_reading_external_records() {
    use std::os::unix::fs::symlink;
    let f = Fixture::<SessionCfg>::new();
    let outside = f.root.join("outside");
    tokio::fs::create_dir_all(&outside).await.unwrap();
    tokio::fs::create_dir_all(&f.binding.data).await.unwrap();
    let directory = f.binding.data.join("agents");
    symlink(&outside, &directory).unwrap();
    Store::<SessionCfg>::load(&f.binding).await.unwrap_err();
    tokio::fs::remove_file(&directory).await.unwrap();
    tokio::fs::create_dir(&directory).await.unwrap();
    let invalid = directory.join("not-an-id.toml");
    tokio::fs::write(&invalid, "invalid").await.unwrap();
    Store::<SessionCfg>::load(&f.binding).await.unwrap_err();
    tokio::fs::remove_file(invalid).await.unwrap();
    let target = outside.join("agent.toml");
    tokio::fs::write(&target, "outside bytes").await.unwrap();
    symlink(&target, directory.join(format!("{FIRST}.toml"))).unwrap();
    Store::<SessionCfg>::load(&f.binding).await.unwrap_err();
    assert_eq!(
        tokio::fs::read_to_string(target).await.unwrap(),
        "outside bytes"
    );
}
