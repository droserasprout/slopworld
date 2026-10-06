use super::*;
use std::sync::Arc;
use tokio::sync::Mutex;

struct Fixture {
    root: PathBuf,
    binding: StorageBinding,
    gate: Arc<Mutex<()>>,
}

impl Fixture {
    fn new() -> Self {
        Self::at(std::env::temp_dir().join(format!("slopd-storage-{}", uuid::Uuid::new_v4())))
    }

    fn at(root: PathBuf) -> Self {
        let binding = StorageBinding::new(
            &root.join("config"),
            &root.join("data"),
            &root.join("config/custom.toml"),
        )
        .unwrap();
        Self {
            root,
            binding,
            gate: Arc::new(Mutex::new(())),
        }
    }

    async fn seed(&self) {
        for (target, text) in [
            (Target::Settings, "old settings"),
            (agent(), "old agent"),
            (sibling(), "untouched"),
        ] {
            crate::paths::write_atomic_async(
                &target.resolve(&self.binding).unwrap(),
                text,
                Some(0o600),
            )
            .await
            .unwrap();
        }
    }

    async fn old_bytes(&self) {
        for (target, text) in [
            (Target::Settings, "old settings"),
            (agent(), "old agent"),
            (sibling(), "untouched"),
        ] {
            assert_eq!(
                read_optional(&target.resolve(&self.binding).unwrap())
                    .await
                    .unwrap()
                    .as_deref(),
                Some(text)
            );
        }
        assert!(!created().resolve(&self.binding).unwrap().exists());
    }
}

impl Drop for Fixture {
    fn drop(&mut self) {
        drop(std::fs::remove_dir_all(&self.root));
    }
}

fn agent() -> Target {
    Target::Data("agents/0123456789abcdef.toml".into())
}
fn sibling() -> Target {
    Target::Data("agents/0123456789abcdee.toml".into())
}
fn created() -> Target {
    Target::Config("projects/0123456789abcdef.toml".into())
}
fn changes() -> Vec<Change> {
    vec![
        Change {
            target: Target::Settings,
            mutation: Mutation::Replace("new settings".into()),
        },
        Change {
            target: agent(),
            mutation: Mutation::Retire,
        },
        Change {
            target: created(),
            mutation: Mutation::Create("new project".into()),
        },
    ]
}

#[tokio::test]
async fn cross_root_commit_publishes_once_after_disk_and_keeps_siblings() {
    let f = Fixture::new();
    f.seed().await;
    let binding = f.binding.clone();
    let value = commit(
        f.binding.clone(),
        changes(),
        f.gate.clone().lock_owned().await,
        move || async move {
            assert_eq!(
                std::fs::read_to_string(&binding.settings).unwrap(),
                "new settings"
            );
            assert!(!agent().resolve(&binding).unwrap().exists());
            assert_eq!(
                std::fs::read_to_string(created().resolve(&binding).unwrap()).unwrap(),
                "new project"
            );
            assert!(!binding.journal().unwrap().exists());
            7
        },
    )
    .await
    .unwrap();
    assert_eq!(value, 7);
    assert_eq!(
        read_optional(&sibling().resolve(&f.binding).unwrap())
            .await
            .unwrap()
            .as_deref(),
        Some("untouched")
    );
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        assert_eq!(
            std::fs::metadata(created().resolve(&f.binding).unwrap())
                .unwrap()
                .permissions()
                .mode()
                & 0o777,
            0o600
        );
    }
}

#[tokio::test]
async fn each_failed_mutation_restores_originals_and_does_not_publish() {
    for index in 0..=3 {
        let f = Fixture::new();
        f.seed().await;
        commit_files(&f.binding, changes(), |current| {
            anyhow::ensure!(current != index, "injected failure");
            Ok(())
        })
        .await
        .unwrap_err();
        f.old_bytes().await;
        assert!(!f.binding.journal().unwrap().exists());
    }
    let f = Fixture::new();
    f.seed().await;
    let fault = crate::paths::fail_writes(&f.binding.settings);
    let published = Arc::new(std::sync::atomic::AtomicBool::new(false));
    let marker = published.clone();
    commit(
        f.binding.clone(),
        changes(),
        f.gate.clone().lock_owned().await,
        move || async move {
            marker.store(true, std::sync::atomic::Ordering::SeqCst);
        },
    )
    .await
    .unwrap_err();
    assert!(!published.load(std::sync::atomic::Ordering::SeqCst));
    drop(fault);
    f.old_bytes().await;
}

async fn interrupted(f: &Fixture) -> String {
    let undo = Undo {
        version: 1,
        binding: f.binding.clone(),
        files: vec![
            Original {
                target: Target::Settings,
                text: Some("old settings".into()),
            },
            Original {
                target: agent(),
                text: Some("old agent".into()),
            },
            Original {
                target: created(),
                text: None,
            },
        ],
    };
    let text = serde_json::to_string(&undo).unwrap();
    crate::paths::write_atomic_async(&f.binding.journal().unwrap(), &text, Some(0o600))
        .await
        .unwrap();
    for change in changes() {
        apply(
            &change.target.resolve(&f.binding).unwrap(),
            &change.mutation,
        )
        .await
        .unwrap();
    }
    text
}

#[tokio::test]
async fn changed_bindings_refuse_all_writes_and_preserve_journal_until_mapping_restored() {
    for change in 0..3 {
        let f = Fixture::new();
        f.seed().await;
        let original_journal = interrupted(&f).await;
        let mut wrong = f.binding.clone();
        match change {
            0 => wrong.config = f.root.join("other-config"),
            1 => {
                // Retain access to the pending journal under a changed data root.
                wrong.data = f.root.join("other-data");
                std::fs::create_dir_all(&wrong.data).unwrap();
                std::fs::hard_link(f.binding.journal().unwrap(), wrong.journal().unwrap()).unwrap();
            }
            _ => wrong.settings = f.root.join("config/other.toml"),
        }
        let guard = f.gate.clone().lock_owned().await;
        let error = recover(&wrong, &guard).await.unwrap_err();
        assert!(error.to_string().contains("restore the original"));
        assert_eq!(
            read_optional(&f.binding.journal().unwrap())
                .await
                .unwrap()
                .as_deref(),
            Some(original_journal.as_str())
        );
        assert_eq!(
            read_optional(&f.binding.settings).await.unwrap().as_deref(),
            Some("new settings")
        );
        assert!(!agent().resolve(&f.binding).unwrap().exists());
        assert!(created().resolve(&f.binding).unwrap().exists());
        recover(&f.binding, &guard).await.unwrap();
        recover(&f.binding, &guard).await.unwrap();
        f.old_bytes().await;
    }
}

#[tokio::test]
async fn malformed_or_escaping_targets_are_rejected_before_any_restore() {
    let f = Fixture::new();
    f.seed().await;
    for target in [
        Target::Data("../escape.toml".into()),
        Target::Data("tasks/../../escape.toml".into()),
        Target::Data("tasks/no-id.toml".into()),
    ] {
        let undo = Undo {
            version: 1,
            binding: f.binding.clone(),
            files: vec![
                Original {
                    target: Target::Settings,
                    text: Some("must not write".into()),
                },
                Original { target, text: None },
            ],
        };
        crate::paths::write_atomic_async(
            &f.binding.journal().unwrap(),
            &serde_json::to_string(&undo).unwrap(),
            Some(0o600),
        )
        .await
        .unwrap();
        let guard = f.gate.clone().lock_owned().await;
        recover(&f.binding, &guard).await.unwrap_err();
        f.old_bytes().await;
        assert!(f.binding.journal().unwrap().exists());
    }
}

#[tokio::test]
async fn creation_collisions_and_duplicate_plans_never_overwrite() {
    let f = Fixture::new();
    f.seed().await;
    commit_files(
        &f.binding,
        vec![Change {
            target: agent(),
            mutation: Mutation::Create("collision".into()),
        }],
        |_| Ok(()),
    )
    .await
    .unwrap_err();
    commit_files(
        &f.binding,
        vec![
            Change {
                target: agent(),
                mutation: Mutation::Replace("one".into()),
            },
            Change {
                target: agent(),
                mutation: Mutation::Retire,
            },
        ],
        |_| Ok(()),
    )
    .await
    .unwrap_err();
    f.old_bytes().await;
    assert!(!f.binding.journal().unwrap().exists());
}

#[tokio::test]
async fn concurrent_atomic_creators_publish_exactly_one_complete_record() {
    let f = Fixture::new();
    let path = created().resolve(&f.binding).unwrap();
    let (left, right) = tokio::join!(
        crate::paths::create_atomic_async(&path, "left", Some(0o600)),
        crate::paths::create_atomic_async(&path, "right", Some(0o600)),
    );
    assert_eq!(usize::from(left.is_ok()) + usize::from(right.is_ok()), 1);
    let expected = if left.is_ok() { "left" } else { "right" };
    assert_eq!(
        read_optional(&path).await.unwrap().as_deref(),
        Some(expected)
    );
}

#[tokio::test]
async fn single_record_retirement_is_idempotent_and_never_scans_siblings() {
    let f = Fixture::new();
    f.seed().await;
    for _ in 0..2 {
        commit_files(
            &f.binding,
            vec![Change {
                target: agent(),
                mutation: Mutation::Retire,
            }],
            |_| Ok(()),
        )
        .await
        .unwrap();
        assert!(!agent().resolve(&f.binding).unwrap().exists());
        assert!(!f.binding.journal().unwrap().exists());
        assert_eq!(
            read_optional(&sibling().resolve(&f.binding).unwrap())
                .await
                .unwrap()
                .as_deref(),
            Some("untouched")
        );
    }
}

#[tokio::test]
async fn unavailable_journal_leaves_all_originals_untouched() {
    let f = Fixture::new();
    f.seed().await;
    let _fault = crate::paths::fail_writes(&f.binding.journal().unwrap());
    commit_files(&f.binding, changes(), |_| Ok(()))
        .await
        .unwrap_err();
    f.old_bytes().await;
    assert!(!f.binding.journal().unwrap().exists());
}

#[cfg(target_os = "linux")]
#[tokio::test]
async fn configuration_and_data_on_different_filesystems_commit_and_recover() {
    use std::os::unix::fs::MetadataExt;
    let mut f = Fixture::new();
    let data = Fixture::at(
        PathBuf::from("/dev/shm").join(format!("slopd-storage-{}", uuid::Uuid::new_v4())),
    );
    f.binding.data = data.binding.data.clone();
    f.seed().await;
    assert_ne!(
        std::fs::metadata(&f.binding.config).unwrap().dev(),
        std::fs::metadata(&f.binding.data).unwrap().dev()
    );
    commit_files(&f.binding, changes(), |_| Ok(()))
        .await
        .unwrap();
    assert_eq!(
        read_optional(&f.binding.settings).await.unwrap().as_deref(),
        Some("new settings")
    );
    // Prepare another interrupted revision, then recover across the same mount boundary.
    tokio::fs::remove_file(created().resolve(&f.binding).unwrap())
        .await
        .unwrap();
    f.seed().await;
    interrupted(&f).await;
    let guard = f.gate.clone().lock_owned().await;
    recover(&f.binding, &guard).await.unwrap();
    f.old_bytes().await;
}

#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn canceled_request_keeps_gate_until_committed_state_is_published() {
    let f = Fixture::new();
    f.seed().await;
    let reached = Arc::new(tokio::sync::Notify::new());
    let published = Arc::new(tokio::sync::Notify::new());
    let release = Arc::new(std::sync::Barrier::new(2));
    let binding = f.binding.clone();
    let gate = f.gate.clone().lock_owned().await;
    let request = tokio::spawn({
        let reached = reached.clone();
        let published = published.clone();
        let release = release.clone();
        async move {
            commit(binding, changes(), gate, move || async move {
                reached.notify_one();
                release.wait();
                published.notify_one();
            })
            .await
        }
    });
    reached.notified().await;
    assert_eq!(
        read_optional(&f.binding.settings).await.unwrap().as_deref(),
        Some("new settings")
    );
    request.abort();
    assert!(request.await.unwrap_err().is_cancelled());
    f.gate.try_lock().unwrap_err();
    release.wait();
    published.notified().await;
    let _finished = f.gate.lock().await;
    assert!(!f.binding.journal().unwrap().exists());
}

#[tokio::test]
async fn rollback_failure_keeps_journal_until_repeated_recovery_succeeds() {
    let f = Fixture::new();
    f.seed().await;
    let blocked = created().resolve(&f.binding).unwrap();
    commit_files(&f.binding, changes(), |index| {
        if index == 2 {
            std::fs::create_dir_all(&blocked)?;
            anyhow::bail!("injected obstruction");
        }
        Ok(())
    })
    .await
    .unwrap_err();
    assert!(f.binding.journal().unwrap().exists());
    let gate = f.gate.clone().lock_owned().await;
    recover(&f.binding, &gate).await.unwrap_err();
    tokio::fs::remove_dir(&blocked).await.unwrap();
    recover(&f.binding, &gate).await.unwrap();
    recover(&f.binding, &gate).await.unwrap();
    f.old_bytes().await;
}

#[cfg(unix)]
#[tokio::test]
async fn aliased_targets_and_journals_cannot_escape_the_bound_roots() {
    let f = Fixture::new();
    f.seed().await;
    let outside = f.root.join("outside");
    std::fs::create_dir_all(&outside).unwrap();
    std::os::unix::fs::symlink(&outside, f.binding.config.join("projects")).unwrap();
    commit_files(&f.binding, changes(), |_| Ok(()))
        .await
        .unwrap_err();
    assert_eq!(
        read_optional(&f.binding.settings).await.unwrap().as_deref(),
        Some("old settings")
    );
    assert!(std::fs::read_dir(&outside).unwrap().next().is_none());
    let journal = f.binding.journal().unwrap();
    std::fs::write(outside.join("journal"), "protected bytes").unwrap();
    std::os::unix::fs::symlink(outside.join("journal"), &journal).unwrap();
    let gate = f.gate.clone().lock_owned().await;
    recover(&f.binding, &gate).await.unwrap_err();
    assert_eq!(
        std::fs::read_to_string(outside.join("journal")).unwrap(),
        "protected bytes"
    );
}

#[tokio::test]
async fn process_crash_recovers_in_a_new_process() {
    const CHILD: &str = "storage::transaction::tests::process_crash_recovers_in_a_new_process";
    if let Ok(stage) = std::env::var("SLOPD_STORAGE_CRASH_STAGE") {
        let f = Fixture::at(PathBuf::from(
            std::env::var_os("SLOPD_STORAGE_CRASH_ROOT").unwrap(),
        ));
        if stage == "interrupt" {
            f.seed().await;
            commit_files(&f.binding, changes(), |index| {
                if index == 1 {
                    std::process::exit(73);
                }
                Ok(())
            })
            .await
            .unwrap();
            panic!("interruption hook did not run");
        }
        assert!(f.binding.journal().unwrap().exists());
        let gate = f.gate.clone().lock_owned().await;
        recover(&f.binding, &gate).await.unwrap();
        recover(&f.binding, &gate).await.unwrap();
        f.old_bytes().await;
        return;
    }
    let f = Fixture::new();
    for (stage, code) in [("interrupt", 73), ("recover", 0)] {
        let output = std::process::Command::new(std::env::current_exe().unwrap())
            .args(["--exact", CHILD, "--nocapture"])
            .env("SLOPD_STORAGE_CRASH_STAGE", stage)
            .env("SLOPD_STORAGE_CRASH_ROOT", &f.root)
            .output()
            .unwrap();
        assert_eq!(
            output.status.code(),
            Some(code),
            "{}\n{}",
            String::from_utf8_lossy(&output.stdout),
            String::from_utf8_lossy(&output.stderr)
        );
    }
}
