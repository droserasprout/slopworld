use super::*;

#[test]
fn candidate_preparation_restores_a_redacted_root_token() {
    let mut old = Config::default();
    old.daemon.token = "real-root-token".into();
    let mut candidate = old.clone();
    candidate.daemon.token = crate::config::TOKEN_REDACTED.into();

    let change = prepare_candidate(&old, candidate).expect("valid candidate");

    assert_eq!(change.new.daemon.token, old.daemon.token);
}

use crate::config::SessionCfg;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use tokio::sync::RwLock;

struct TestRuntime {
    accepted: RwLock<Config>,
    reject: AtomicBool,
    publications: AtomicUsize,
    endpoint_pause: Mutex<Option<(Arc<tokio::sync::Notify>, Arc<tokio::sync::Notify>)>>,
    endpoint: Mutex<String>,
    publish_pause: Mutex<Option<(Arc<tokio::sync::Notify>, Arc<tokio::sync::Notify>)>>,
}

impl Runtime for TestRuntime {
    async fn snapshot(&self) -> Config {
        self.accepted.read().await.clone()
    }
    async fn validate(&self, _old: &Config, _new: &Config) -> Result<()> {
        anyhow::ensure!(
            !self.reject.load(Ordering::Acquire),
            "runtime rejected candidate"
        );
        Ok(())
    }
    fn worktrees(&self) -> crate::worktrees::Store {
        crate::worktrees::Store::default()
    }
    async fn publish(&self, change: ConfigChange) -> String {
        let token = change.new.daemon.token.clone();
        let pause = self.publish_pause.lock().unwrap().take();
        if let Some((started, release)) = pause {
            started.notify_one();
            release.notified().await;
        }
        *self.accepted.write().await = change.new;
        self.publications.fetch_add(1, Ordering::AcqRel);
        token
    }
    async fn update_endpoint(&self, token: &str) {
        let pause = self.endpoint_pause.lock().unwrap().take();
        if let Some((started, release)) = pause {
            started.notify_one();
            release.notified().await;
        }
        *self.endpoint.lock().unwrap() = token.to_owned();
    }
}

/// Uses production stores without constructing live sessions or tmux.
pub(super) struct Fixture {
    pub(super) cfg_path: PathBuf,
    owner: Transactions,
    runtime: TestRuntime,
}
impl Fixture {
    pub(super) fn new(mut cfg: Config) -> Self {
        if cfg.daemon.token.trim().is_empty() {
            cfg.daemon.token = "test-root".into();
        }
        let root =
            std::env::temp_dir().join(format!("slopd-config-owner-{}", uuid::Uuid::new_v4()));
        std::fs::create_dir_all(&root).unwrap();
        let cfg_path = root.join("config.toml");
        let records = records::Records::fixture(&mut cfg, &cfg_path).unwrap();
        let owner = Transactions::new(records, crate::config::catalog::revision(&cfg_path).ok());
        Self {
            cfg_path,
            owner,
            runtime: TestRuntime {
                accepted: RwLock::new(cfg),
                reject: AtomicBool::new(false),
                publications: AtomicUsize::new(0),
                endpoint_pause: Mutex::new(None),
                endpoint: Mutex::new(String::new()),
                publish_pause: Mutex::new(None),
            },
        }
    }
    pub(super) async fn config(&self) -> Config {
        self.runtime.snapshot().await
    }
    pub(super) async fn update_cfg(
        &self,
        mutation: ConfigMutation,
        update: impl FnOnce(&mut Config) -> Result<()>,
    ) -> Result<()> {
        let ((), prepared) = self
            .owner
            .prepare(&self.runtime, mutation, |cfg| {
                update(cfg).map(|()| ((), true))
            })
            .await?;
        self.owner.commit(&self.runtime, prepared.unwrap()).await
    }
}
impl Drop for Fixture {
    fn drop(&mut self) {
        drop(std::fs::remove_dir_all(self.cfg_path.parent().unwrap()));
    }
}

fn agent_config() -> Config {
    Config {
        sessions: vec![SessionCfg {
            name: "agent".into(),
            ..Default::default()
        }],
        ..Default::default()
    }
}

#[tokio::test]
async fn dropping_preparation_releases_gate_without_disk_or_runtime_effects() {
    let f = Fixture::new(agent_config());
    let before = f.config().await;
    let path = f
        .owner
        .records
        .binding
        .data
        .join("agents")
        .join(format!("{}.toml", before.sessions[0].state_id));
    let bytes = std::fs::read(&path).unwrap();
    let ((), prepared) = f
        .owner
        .prepare(&f.runtime, ConfigMutation::Agents, |cfg| {
            cfg.sessions[0].label = Some("discarded".into());
            Ok(((), true))
        })
        .await
        .unwrap();
    f.owner.persist.try_lock().unwrap_err();
    drop(prepared);
    drop(f.owner.persist.try_lock().unwrap());
    assert_eq!(std::fs::read(path).unwrap(), bytes);
    assert_eq!(f.config().await.sessions[0].label, None);
    assert_eq!(f.runtime.publications.load(Ordering::Acquire), 0);
    f.update_cfg(ConfigMutation::Agents, |cfg| {
        cfg.sessions[0].label = Some("retry".into());
        Ok(())
    })
    .await
    .unwrap();
    assert_eq!(f.config().await.sessions[0].label.as_deref(), Some("retry"));
}

#[tokio::test]
async fn runtime_rejection_and_unchanged_edits_release_gate_without_publication() {
    let f = Fixture::new(agent_config());
    f.runtime.reject.store(true, Ordering::Release);
    let result = f
        .update_cfg(ConfigMutation::Agents, |cfg| {
            cfg.sessions[0].label = Some("rejected".into());
            Ok(())
        })
        .await;
    assert!(result.unwrap_err().to_string().contains("runtime rejected"));
    drop(f.owner.persist.try_lock().unwrap());
    let (value, prepared) = f
        .owner
        .prepare(&f.runtime, ConfigMutation::Agents, |cfg| {
            cfg.sessions.clear();
            Ok((42, false))
        })
        .await
        .unwrap();
    assert_eq!(value, 42);
    assert!(prepared.is_none());
    drop(f.owner.persist.try_lock().unwrap());
    assert_eq!(f.config().await.sessions.len(), 1);
    assert_eq!(f.runtime.publications.load(Ordering::Acquire), 0);
}

#[tokio::test]
async fn persistence_gate_remains_held_until_runtime_publication_finishes() {
    let f = Arc::new(Fixture::new(agent_config()));
    let started = Arc::new(tokio::sync::Notify::new());
    let release = Arc::new(tokio::sync::Notify::new());
    *f.runtime.publish_pause.lock().unwrap() = Some((started.clone(), release.clone()));
    let worker = f.clone();
    let task = tokio::spawn(async move {
        worker
            .update_cfg(ConfigMutation::Agents, |cfg| {
                cfg.sessions[0].label = Some("committed".into());
                Ok(())
            })
            .await
    });
    started.notified().await;
    f.owner.persist.try_lock().unwrap_err();
    assert_eq!(f.config().await.sessions[0].label, None);
    let disk = crate::storage::workspace::Store::<SessionCfg>::load(&f.owner.records.binding)
        .await
        .unwrap();
    assert_eq!(disk.ordered()[0].label.as_deref(), Some("committed"));
    release.notify_one();
    task.await.unwrap().unwrap();
    drop(f.owner.persist.try_lock().unwrap());
    assert_eq!(
        f.config().await.sessions[0].label.as_deref(),
        Some("committed")
    );
}

#[tokio::test]
async fn prepared_change_cannot_commit_through_another_owner() {
    let f = Fixture::new(agent_config());
    let other = Fixture::new(agent_config());
    let ((), prepared) = f
        .owner
        .prepare(&f.runtime, ConfigMutation::Agents, |cfg| {
            cfg.sessions[0].label = Some("wrong owner".into());
            Ok(((), true))
        })
        .await
        .unwrap();
    let error = other
        .owner
        .commit(&other.runtime, prepared.unwrap())
        .await
        .unwrap_err();
    assert!(error.to_string().contains("another transaction owner"));
    drop(f.owner.persist.try_lock().unwrap());
    assert_eq!(f.config().await.sessions[0].label, None);
    assert_eq!(other.config().await.sessions[0].label, None);
    assert_eq!(f.runtime.publications.load(Ordering::Acquire), 0);
    assert_eq!(other.runtime.publications.load(Ordering::Acquire), 0);
}

#[tokio::test]
async fn failed_recovery_prevents_candidate_edit_and_can_be_retried() {
    let f = Fixture::new(agent_config());
    let journal = f.owner.records.binding.journal().unwrap();
    std::fs::write(&journal, "invalid journal").unwrap();
    let result = f
        .owner
        .prepare(
            &f.runtime,
            ConfigMutation::Agents,
            |_| -> Result<((), bool)> {
                panic!("an edit must not observe unrecovered files");
            },
        )
        .await;
    assert!(result.is_err());
    drop(f.owner.persist.try_lock().unwrap());
    assert_eq!(f.runtime.publications.load(Ordering::Acquire), 0);
    std::fs::remove_file(journal).unwrap();
    f.update_cfg(ConfigMutation::Agents, |cfg| {
        cfg.sessions[0].label = Some("recovered".into());
        Ok(())
    })
    .await
    .unwrap();
    assert_eq!(
        f.config().await.sessions[0].label.as_deref(),
        Some("recovered")
    );
}

#[tokio::test]
async fn settings_keep_gate_through_endpoint_update_and_preserve_workspace() {
    let f = Arc::new(Fixture::new(agent_config()));
    let accepted = f.config().await;
    let stamp = f.owner.library_revision.lock().unwrap().clone();
    let started = Arc::new(tokio::sync::Notify::new());
    let release = Arc::new(tokio::sync::Notify::new());
    *f.runtime.endpoint_pause.lock().unwrap() = Some((started.clone(), release.clone()));
    let worker = f.clone();
    let task = tokio::spawn(async move {
        worker
            .owner
            .edit_settings(
                &worker.runtime,
                SettingsEdit::Patch(toml::from_str("[daemon]\ntoken = 'rotated'").unwrap()),
            )
            .await
    });
    started.notified().await;
    f.owner.persist.try_lock().unwrap_err();
    assert_eq!(f.config().await.daemon.token, "rotated");
    assert_eq!(
        Config::load(&f.cfg_path).await.unwrap().daemon.token,
        "rotated"
    );
    assert!(f.runtime.endpoint.lock().unwrap().is_empty());
    release.notify_one();
    task.await.unwrap().unwrap();
    drop(f.owner.persist.try_lock().unwrap());
    assert_eq!(*f.runtime.endpoint.lock().unwrap(), "rotated");
    assert_eq!(
        f.config().await.sessions[0].state_id,
        accepted.sessions[0].state_id
    );
    assert_eq!(*f.owner.library_revision.lock().unwrap(), stamp);
}
