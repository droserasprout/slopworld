//! TODO(remove after user tests and approves workspace store migration):
//! priv/notes/plan-storage-main.md. All inputs are disposable; never user stores.
use super::*;
use std::path::{Path, PathBuf};

struct Fixture {
    root: PathBuf,
    binding: StorageBinding,
}
impl Fixture {
    fn at(root: PathBuf) -> Self {
        let binding = StorageBinding::new(
            &root.join("config"),
            &root.join("data"),
            &root.join("custom/root.toml"),
        )
        .unwrap();
        Self { root, binding }
    }
    fn new() -> Self {
        Self::at(std::env::temp_dir().join(format!("slopd-migrate-{}", uuid::Uuid::new_v4())))
    }
    fn write(&self, target: Target, text: &str) {
        crate::paths::write_private_toml(&target.resolve(&self.binding).unwrap(), text).unwrap();
    }
    fn seed(&self) {
        self.write(
            Target::Settings,
            r#"# retain root comment
future = "root extension"
[daemon]
bind = "127.0.0.1:7717"
token = "retained-secret"
[[project]]
id = "aaaaaaaaaaaaaaaa"
name = "repo"
dir = "/tmp"
future = "project extension"
[[session]]
state_id = "bbbbbbbbbbbbbbbb"
name = "agent"
project = "repo"
cmd = "true"
worktree = "main"
future = "agent extension"
[[host_terminal]]
name = "shell"
project = "repo"
future = "shell extension"
"#,
        );
        self.write(
            Target::Legacy("tasks.toml".into()),
            &format!("generation = 2\n[[tasks]]\n{}", task_text()),
        );
        self.write(Target::Legacy("tasks.journal".into()), "{\"generation\":2,\"update\":{\"id\":\"0000000000000001\",\"status\":\"done\",\"note\":\"completed\",\"summary\":null,\"updated_ms\":3}}\n");
        self.write(Target::Legacy("worktrees.toml".into()), "worktrees = []\n");
        self.write(Target::Legacy("grants.toml".into()), "grants = []\n");
    }
}
impl Drop for Fixture {
    fn drop(&mut self) {
        drop(std::fs::remove_dir_all(&self.root));
    }
}
fn task_text() -> &'static str {
    "id = '0000000000000001'\nfrom = 'host'\nto = 'agent'\nfrom_id = 'host'\nto_id = 'bbbbbbbbbbbbbbbb'\nbody = 'work'\nstatus = 'queued'\ncreated_ms = 1\nupdated_ms = 1\nfuture = 'task extension'\n"
}
fn bytes(root: &Path) -> std::collections::BTreeMap<PathBuf, Vec<u8>> {
    let mut result = std::collections::BTreeMap::new();
    if let Ok(entries) = std::fs::read_dir(root) {
        for entry in entries {
            let path = entry.unwrap().path();
            if path.is_dir() {
                result.extend(bytes(&path));
            } else {
                result.insert(path.clone(), std::fs::read(path).unwrap());
            }
        }
    }
    result
}

#[tokio::test]
async fn migration_preserves_records_extensions_order_root_and_reruns_without_writes() {
    let f = Fixture::new();
    f.seed();
    super::super::layout::load(&f.binding).await.unwrap_err();
    run(&f.binding).await.unwrap();
    let cfg = super::super::layout::load(&f.binding).await.unwrap();
    assert_eq!(cfg.sessions[0].state_id, "bbbbbbbbbbbbbbbb");
    assert!(crate::storage_id::valid(&cfg.host_terminals[0].id));
    assert_eq!(cfg.daemon.token, "retained-secret");
    let root = std::fs::read_to_string(&f.binding.settings).unwrap();
    assert!(root.contains("# retain root comment"));
    assert!(root.contains("root extension"));
    assert!(!root.contains("[[session]]"));
    for directory in ["agents", "host_shells", "tasks"] {
        let entry = std::fs::read_dir(f.binding.data.join(directory))
            .unwrap()
            .next()
            .unwrap()
            .unwrap();
        assert!(
            std::fs::read_to_string(entry.path())
                .unwrap()
                .contains("extension")
        );
    }
    let tasks = crate::tasks::Tasks::load_records(&f.binding.data).unwrap();
    assert_eq!(tasks.all()[0].note.as_deref(), Some("completed"));
    let accepted = bytes(&f.root);
    run(&f.binding).await.unwrap();
    assert_eq!(bytes(&f.root), accepted);
}

#[tokio::test]
async fn every_migration_write_and_retirement_failure_restores_all_original_bytes() {
    let f = Fixture::new();
    f.seed();
    let original = bytes(&f.root);
    let plan = prepare(&f.binding).await.unwrap();
    for fail in 0..=plan.len() {
        let result = transaction::commit_files(&f.binding, plan.clone(), |index| {
            anyhow::ensure!(index != fail, "injected migration write failure");
            Ok(())
        })
        .await;
        result.unwrap_err();
        assert_eq!(bytes(&f.root), original, "failure {fail}");
        let gate = Arc::new(tokio::sync::Mutex::new(())).lock_owned().await;
        transaction::recover(&f.binding, &gate).await.unwrap();
    }
    run(&f.binding).await.unwrap();
}

#[tokio::test]
async fn malformed_inputs_and_mixed_authority_abort_without_mutation() {
    for (target, bad) in [
        (Target::Legacy("tasks.toml".into()), "invalid = ["),
        (Target::Legacy("tasks.journal".into()), "{\"generation\":2"),
        (
            Target::Legacy("worktrees.toml".into()),
            "worktrees = 'invalid'",
        ),
        (
            Target::Legacy("grants.toml".into()),
            "[[grants]]\ntoken='invalid'",
        ),
        (Target::Data("grants.toml".into()), "grants = []"),
        (
            Target::Data("agents/cccccccccccccccc.toml".into()),
            "occupied = true",
        ),
    ] {
        let f = Fixture::new();
        f.seed();
        f.write(target, bad);
        let before = bytes(&f.root);
        run(&f.binding).await.unwrap_err();
        assert_eq!(bytes(&f.root), before);
    }
}

#[test]
fn replay_is_read_only_strict_and_preserves_legacy_generation_semantics() {
    let snapshot = format!("generation = 2\n[[tasks]]\n{}", task_text());
    let update = |generation| {
        format!(
            "{{\"generation\":{generation},\"update\":{{\"id\":\"0000000000000001\",\"status\":\"done\",\"updated_ms\":8}}}}\n"
        )
    };
    let rows = tasks::decode(Some(&snapshot), Some(&update(1))).unwrap();
    assert_eq!(rows[0]["status"].as_str(), Some("queued"));
    let rows = tasks::decode(Some(&snapshot), Some(&update(2))).unwrap();
    assert_eq!(rows[0]["status"].as_str(), Some("done"));
    tasks::decode(Some("tasks = false"), None).unwrap_err();
    tasks::decode(None, Some("{}\n")).unwrap_err();
    tasks::decode(None, Some("invalid\n")).unwrap_err();
    assert!(tasks::decode(None, Some(&update(0))).unwrap().is_empty());
    let task: toml::Value = toml::from_str(task_text()).unwrap();
    let create = format!("{}\n", serde_json::json!({"generation":0,"create":task}));
    assert_eq!(tasks::decode(None, Some(&create)).unwrap().len(), 1);
}

#[tokio::test]
async fn interrupted_migration_child() {
    let Some(root) = std::env::var_os("SLOPD_MIGRATION_TEST_CHILD") else {
        return;
    };
    let f = Fixture::at(root.into());
    let plan = prepare(&f.binding).await.unwrap();
    transaction::commit_files(&f.binding, plan, |index| {
        if index == 3 {
            std::process::exit(77);
        }
        Ok(())
    })
    .await
    .unwrap();
    panic!("interruption was not reached");
}

#[tokio::test]
async fn new_process_recovery_keeps_fixed_targets_until_full_rollback() {
    let f = Fixture::new();
    f.seed();
    let original = bytes(&f.root);
    let status = std::process::Command::new(std::env::current_exe().unwrap())
        .args([
            "--exact",
            "storage::migration::tests::interrupted_migration_child",
            "--nocapture",
        ])
        .env("SLOPD_MIGRATION_TEST_CHILD", &f.root)
        .status()
        .unwrap();
    assert_eq!(status.code(), Some(77));
    let journal = f.binding.journal().unwrap();
    assert!(journal.exists());
    let interrupted = bytes(&f.root);
    let gate = Arc::new(tokio::sync::Mutex::new(())).lock_owned().await;
    let changed = StorageBinding::new(
        &f.root.join("other-config"),
        &f.binding.data,
        &f.binding.settings,
    )
    .unwrap();
    transaction::recover(&changed, &gate).await.unwrap_err();
    assert_eq!(bytes(&f.root), interrupted);
    // A blocked rollback retains its original journal and allocations across retries.
    let agent = f.binding.data.join("agents/bbbbbbbbbbbbbbbb.toml");
    std::fs::remove_file(&agent).unwrap();
    std::fs::create_dir(&agent).unwrap();
    transaction::recover(&f.binding, &gate).await.unwrap_err();
    assert!(journal.exists());
    std::fs::remove_dir(&agent).unwrap();
    transaction::recover(&f.binding, &gate).await.unwrap();
    assert_eq!(bytes(&f.root), original);
    run(&f.binding).await.unwrap();
    let committed = bytes(&f.root);
    run(&f.binding).await.unwrap();
    assert_eq!(bytes(&f.root), committed);
}

#[tokio::test]
async fn migration_keeps_credentials_and_all_checkout_phases_without_git_effects() {
    use std::os::unix::fs::PermissionsExt;
    let f = Fixture::new();
    f.seed();
    let grants = "[[grants]]\ntoken='0123456789abcdef0123456789abcdef'\ngrantor='agent'\ngrantor_state_id='bbbbbbbbbbbbbbbb'\nsessions=['agent']\nsession_state_ids={agent='bbbbbbbbbbbbbbbb'}\nlevel='rw'\n";
    f.write(Target::Legacy("grants.toml".into()), grants);
    let mut worktrees = String::new();
    for (index, phase) in ["ready", "allocating", "relocating", "error"]
        .into_iter()
        .enumerate()
    {
        worktrees.push_str(&format!("[[worktrees]]\nid='{index:016x}'\nproject_id='aaaaaaaaaaaaaaaa'\nname='tree{index}'\npath='/nonexistent/migration/tree{index}'\nrepository='/nonexistent/migration/repo'\nmanaged=true\ninitial_branch='main'\nbase='abc'\nphase='{phase}'\nerror='retained recovery details'\nfuture='extension'\n"));
    }
    f.write(Target::Legacy("worktrees.toml".into()), &worktrees);
    let mut root = std::fs::read_to_string(&f.binding.settings).unwrap();
    root.push_str("\n[[project]]\nname='missing-id'\ndir='/tmp'\n");
    f.write(Target::Settings, &root);
    run(&f.binding).await.unwrap();
    let cfg = super::super::layout::load(&f.binding).await.unwrap();
    assert!(crate::storage_id::valid(&cfg.projects[1].id));
    let grant_path = f.binding.data.join("grants.toml");
    assert_eq!(std::fs::read_to_string(&grant_path).unwrap(), grants);
    assert_eq!(
        std::fs::metadata(&grant_path).unwrap().permissions().mode() & 0o777,
        0o600
    );
    let trees = super::super::workspace::Store::<crate::worktrees::Worktree>::load(&f.binding)
        .await
        .unwrap()
        .ordered();
    assert_eq!(
        trees.iter().map(|w| w.phase.as_str()).collect::<Vec<_>>(),
        ["ready", "allocating", "relocating", "error"]
    );
    assert!(trees.iter().all(|w| w.error == "retained recovery details"));
}

#[tokio::test]
async fn duplicate_names_and_displaced_catalogs_are_reported_before_writes() {
    let f = Fixture::new();
    f.seed();
    let original = std::fs::read_to_string(&f.binding.settings).unwrap();
    f.write(Target::Settings, &format!("{original}\n[[session]]\nname='agent'\nstate_id='cccccccccccccccc'\nproject='repo'\ncmd='true'\n"));
    let before = bytes(&f.root);
    run(&f.binding).await.unwrap_err();
    assert_eq!(bytes(&f.root), before);
    f.write(Target::Settings, &original);
    let catalog = f
        .binding
        .settings
        .parent()
        .unwrap()
        .join("prompts/old.toml");
    crate::paths::write_private_toml(&catalog, "name='old'\ntext='retain'\n").unwrap();
    let before = bytes(&f.root);
    let error = run(&f.binding).await.unwrap_err();
    assert!(error.to_string().contains("outside SLOPD_CONFIG_ROOT"));
    assert_eq!(bytes(&f.root), before);
}

#[tokio::test]
async fn endpoint_exclusion_covers_the_old_address_before_transaction_recovery() {
    let f = Fixture::new();
    let occupied = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
    let old = format!(
        "[daemon]\nbind='{}'\ntoken='original'\n",
        occupied.local_addr().unwrap()
    );
    f.write(
        Target::Settings,
        "[daemon]\nbind='127.0.0.1:0'\ntoken='interrupted'\n",
    );
    let journal = crate::config::Config::recovery_path_for(&f.binding.settings);
    let original = serde_json::json!({"files":{"root.toml":old}});
    crate::paths::write_private_toml(&journal, &original.to_string()).unwrap();
    let before = bytes(&f.root);
    super::super::startup::reserve(&f.binding)
        .await
        .unwrap_err();
    assert_eq!(bytes(&f.root), before);
    drop(occupied);
    let reserved = super::super::startup::reserve(&f.binding).await.unwrap();
    run(&f.binding).await.unwrap();
    assert_eq!(std::fs::read_to_string(&f.binding.settings).unwrap(), old);
    assert!(!journal.exists());
    drop(reserved);
}

#[cfg(target_os = "linux")]
#[tokio::test]
async fn migration_commits_across_filesystems_without_cross_device_renames() {
    use std::os::unix::fs::MetadataExt;
    let mut f = Fixture::new();
    let data = Fixture::at(
        PathBuf::from("/dev/shm").join(format!("slopd-migrate-data-{}", uuid::Uuid::new_v4())),
    );
    f.binding = StorageBinding::new(
        &f.binding.config,
        &data.root.join("data"),
        &f.binding.settings,
    )
    .unwrap();
    f.seed();
    std::fs::create_dir_all(&f.binding.data).unwrap();
    assert_ne!(
        std::fs::metadata(f.binding.settings.parent().unwrap())
            .unwrap()
            .dev(),
        std::fs::metadata(&f.binding.data).unwrap().dev()
    );
    run(&f.binding).await.unwrap();
    let cfg = super::super::layout::load(&f.binding).await.unwrap();
    assert_eq!(cfg.sessions[0].state_id, "bbbbbbbbbbbbbbbb");
    assert_eq!(
        crate::tasks::Tasks::load_records(&f.binding.data)
            .unwrap()
            .all()
            .len(),
        1
    );
    run(&f.binding).await.unwrap();
}

#[tokio::test]
async fn undo_can_narrow_a_wildcard_bind_without_self_conflict() {
    let f = Fixture::new();
    let free = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
    let port = free.local_addr().unwrap().port();
    drop(free);
    let old_address = format!("127.0.0.1:{port}");
    let old = format!("[daemon]\nbind='{old_address}'\n");
    f.write(
        Target::Settings,
        &format!("[daemon]\nbind='0.0.0.0:{port}'\n"),
    );
    let journal = crate::config::Config::recovery_path_for(&f.binding.settings);
    crate::paths::write_private_toml(
        &journal,
        &serde_json::json!({"files":{"root.toml":old}}).to_string(),
    )
    .unwrap();
    let reserved = super::super::startup::reserve(&f.binding).await.unwrap();
    run(&f.binding).await.unwrap();
    let listener = super::super::startup::serving_listener(reserved, &old_address)
        .await
        .unwrap();
    assert_eq!(
        listener.local_addr().unwrap().ip(),
        std::net::Ipv4Addr::LOCALHOST
    );
}
