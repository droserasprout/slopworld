//! Task-owned worker creation. A worker is a normal sandboxed session with explicit ownership
//! metadata and a durable mailbox task; its name is never used to reconstruct that relationship.

use super::super::*;

use anyhow::anyhow;

pub(crate) const WORKER_SANDBOX: &str = "slopworld-worker";
pub(crate) const WORKER_DISCOVERY_BREADCRUMB: &str =
    "Worker task: use `$SLOPWORLD_TASK_ID` with `slopctl task`, then `accept`, `progress`, and finally `finish` or `fail`. Do not search the inbox or poll task status.";

#[derive(Debug, Clone)]
pub struct WorkerSpawn {
    pub task: crate::tasks::Task,
    pub session: String,
}

impl Manager {
    /// Create the mailbox record first, then the child session, then launch it. A failed launch
    /// leaves the task and (for durable workers) the stopped session visible so a restart or
    /// operator can diagnose it instead of losing a half-created child between two API calls.
    pub async fn spawn_worker(
        self: &Arc<Self>,
        caller: String,
        parent: String,
        body: String,
        durable: bool,
    ) -> Result<WorkerSpawn> {
        let _spawn = self.worker_spawn.lock().await;
        self.reload_if_changed().await;
        let cfg = self.config().await;
        let caller = caller.trim().to_string();
        if caller.is_empty() {
            bail!("a worker needs a caller identity");
        }
        if caller != crate::tasks::HOST && !self.session_known(&caller).await {
            bail!("no such caller session: {caller}");
        }
        let parent = parent.trim().to_string();
        if parent.is_empty() {
            bail!("a worker needs a parent session");
        }
        let parent_session = self
            .session_cfg(&parent)
            .await
            .ok_or_else(|| anyhow!("no such parent session: {parent}"))?;
        if self.is_host(&parent).await {
            bail!("a host terminal cannot own a worker; choose an agent parent");
        }

        let project = parent_session.project.trim();
        if project.is_empty() {
            bail!("parent {parent} has no project");
        }
        let p = self
            .project_for(&cfg, &parent_session)
            .await
            .ok_or_else(|| anyhow!("no such worker project: {project}"))?;

        // PARENT supplies the behavior to clone; the invoking session owns the child in the
        // task mailbox and sidebar.
        let mut session = clone_worker_session(
            parent_session,
            self.fresh_worker_name(&caller).await,
            &caller,
        );
        // A worker must be able to reach the daemon and must carry the exact API-capability
        // preset. It is appended to the parent's sandbox rather than replacing its normal tool
        // and state configuration.
        if cfg.network_of(&session, &p) == NetworkMode::None {
            bail!("worker parent {parent} disables networking; task API access needs a network");
        }
        let table = crate::presets::table();
        crate::sandbox::validate_preset_name(WORKER_SANDBOX, &table)
            .context("worker task API preset is invalid")?;
        if cfg.command_of(&session).trim().is_empty() {
            bail!("worker parent {parent} does not resolve to an executable command");
        }

        let task = self.tasks.create_worker(
            caller.clone(),
            session.name.clone(),
            body,
            caller,
            durable,
        )?;
        self.spawn_task_summary_request(task.clone());
        session.task_id = task.id.clone();

        if durable {
            let persisted = self
                .update_cfg(|cfg| {
                    if cfg.session(&session.name).is_some()
                        || cfg
                            .host_terminals
                            .iter()
                            .any(|tab| tab.name == session.name)
                    {
                        bail!("worker name became unavailable: {}", session.name);
                    }
                    cfg.sessions.push(session.clone());
                    Ok(())
                })
                .await;
            if let Err(error) = persisted {
                self.fail_worker_task(
                    &task.id,
                    format!("could not persist worker session: {error:#}"),
                );
                return Err(error);
            }
            self.sync_from_config().await;
        } else {
            let mut live = Live::new(session.clone(), TitleCapture::default());
            live.ephemeral = true;
            self.live.write().await.insert(session.name.clone(), live);
        }

        if let Err(error) = self.start(&session.name).await {
            self.fail_worker_task(&task.id, format!("worker failed to start: {error:#}"));
            if !durable {
                self.forget(&session.name).await;
            }
            return Err(error);
        }

        self.emit(Event::Sessions {
            sessions: self.views().await,
        });
        let manager = self.clone();
        let name = session.name.clone();
        let bootstrap = cfg.daemon.instructions.worker_prompt.clone();
        tokio::spawn(async move { manager.deliver(&name, &bootstrap, Vec::new()).await });
        Ok(WorkerSpawn {
            task,
            session: session.name,
        })
    }

    async fn fresh_worker_name(&self, owner: &str) -> String {
        let base = format!("{owner}-worker");
        let cfg = self.config().await;
        let live = self.live.read().await;
        if !live.contains_key(&base)
            && cfg.session(&base).is_none()
            && !cfg.host_terminals.iter().any(|tab| tab.name == base)
        {
            return base;
        }
        (2..)
            .map(|n| format!("{base}-{n}"))
            .find(|name| {
                !live.contains_key(name)
                    && cfg.session(name).is_none()
                    && !cfg.host_terminals.iter().any(|tab| tab.name == *name)
            })
            .unwrap_or_else(|| format!("{base}-{}", uuid::Uuid::new_v4()))
    }
}

/// Duplicate the parent's session behavior for a child. A worker gets a new private-state
/// identity and daemon-owned hierarchy metadata, while command, project, sandbox, prompts,
/// networking, limits and mounts remain the parent's choices. Workers are deliberately stopped
/// after their task exits: retrying a task is an explicit operator decision, not a daemon loop.
fn clone_worker_session(mut parent: SessionCfg, name: String, owner_name: &str) -> SessionCfg {
    parent.name = name;
    parent.state_id = uuid::Uuid::new_v4().to_string();
    parent.worker = true;
    parent.parent = owner_name.to_string();
    parent.task_id.clear();
    parent.autostart = false;
    parent.auto_resume = false;
    parent.worker_token = None;
    if !parent.sandbox.iter().any(|preset| preset == WORKER_SANDBOX) {
        parent.sandbox.push(WORKER_SANDBOX.into());
    }
    parent
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::config::{
        Config, Daemon, DnsConfig, Limits, Mount, MountMode, ProjectCfg, SessionCfg,
    };

    #[tokio::test]
    async fn worker_names_are_explicitly_coined_without_name_inference() {
        let manager = crate::session::test_manager(Config {
            daemon: Daemon::default(),
            projects: vec![ProjectCfg {
                name: "repo".into(),
                dir: "/tmp".into(),
                ..Default::default()
            }],
            sessions: vec![SessionCfg {
                name: "parent".into(),
                project: "repo".into(),
                ..Default::default()
            }],
            ..Default::default()
        });
        assert_eq!(manager.fresh_worker_name("parent").await, "parent-worker");
        manager.live.write().await.insert(
            "parent-worker".into(),
            Live::new(SessionCfg::default(), TitleCapture::default()),
        );
        assert_eq!(manager.fresh_worker_name("parent").await, "parent-worker-2");

        // The selected parent supplies the clone, but the caller owns the child in the sidebar.
        assert_eq!(manager.fresh_worker_name("caller").await, "caller-worker");
    }

    #[tokio::test]
    async fn invalid_worker_parent_is_rejected_before_task_creation() {
        let manager = crate::session::test_manager(Config::default());

        let error = manager
            .spawn_worker(
                crate::tasks::HOST.into(),
                "missing-parent".into(),
                "inspect the build".into(),
                false,
            )
            .await
            .unwrap_err()
            .to_string();

        assert!(
            error.contains("no such parent session: missing-parent"),
            "{error}"
        );
        assert!(manager.all_tasks().is_empty());
    }

    #[test]
    fn worker_clones_parent_settings_and_only_replaces_child_metadata() {
        let parent = SessionCfg {
            name: "parent".into(),
            label: Some("Research".into()),
            state_id: "parent-state".into(),
            project: "repo".into(),
            command: "codex".into(),
            cmd: Some("codex --full-auto".into()),
            sandbox: vec!["codex".into(), "gpu".into()],
            breadcrumbs: vec!["tips".into()],
            slopworld_md: true,
            instructions_breadcrumb: false,
            persistent_tmp: true,
            breadcrumb_yolo: false,
            network: Some(NetworkMode::Private),
            dns: Some(DnsConfig::Resolved),
            limits: Limits {
                memory_mb: Some(1024),
                pids: Some(64),
                nofile: Some(128),
                cpu_pct: Some(75),
            },
            mounts: vec![Mount {
                project: "other".into(),
                mode: MountMode::Ro,
            }],
            autostart: true,
            auto_resume: true,
            worker: false,
            parent: String::new(),
            task_id: String::new(),
            worker_token: None,
        };
        let child = clone_worker_session(parent.clone(), "caller-worker".into(), "caller");

        assert_eq!(child.name, "caller-worker");
        assert_ne!(child.state_id, parent.state_id);
        assert!(child.worker);
        assert_eq!(child.parent, "caller");
        assert!(child.task_id.is_empty());
        assert_eq!(child.label, parent.label);
        assert_eq!(child.project, parent.project);
        assert_eq!(child.command, parent.command);
        assert_eq!(child.cmd, parent.cmd);
        assert_eq!(child.sandbox, ["codex", "gpu", WORKER_SANDBOX]);
        assert_eq!(child.breadcrumbs, parent.breadcrumbs);
        assert_eq!(child.slopworld_md, parent.slopworld_md);
        assert_eq!(
            child.instructions_breadcrumb,
            parent.instructions_breadcrumb
        );
        assert_eq!(child.persistent_tmp, parent.persistent_tmp);
        assert_eq!(child.breadcrumb_yolo, parent.breadcrumb_yolo);
        assert_eq!(child.network, parent.network);
        assert_eq!(child.dns, parent.dns);
        assert_eq!(child.limits, parent.limits);
        assert_eq!(child.mounts, parent.mounts);
        assert!(!child.autostart);
        assert!(!child.auto_resume);
    }

    #[tokio::test]
    async fn durable_worker_parent_survives_a_daemon_restart() {
        let dir = std::env::temp_dir().join(format!(
            "slopd-worker-restart-{}-{}",
            std::process::id(),
            uuid::Uuid::new_v4()
        ));
        let project_dir = dir.join("repo");
        std::fs::create_dir_all(&project_dir).unwrap();

        let config = Config {
            projects: vec![ProjectCfg {
                name: "repo".into(),
                dir: project_dir.to_string_lossy().into_owned(),
                ..Default::default()
            }],
            sessions: vec![
                SessionCfg {
                    name: "caller".into(),
                    project: "repo".into(),
                    ..Default::default()
                },
                SessionCfg {
                    name: "caller-worker".into(),
                    project: "repo".into(),
                    worker: true,
                    parent: "caller".into(),
                    task_id: "task-7".into(),
                    ..Default::default()
                },
            ],
            ..Default::default()
        };
        let config_path = dir.join("config.toml");
        config.save(&config_path).await.unwrap();

        // A new daemon reads the durable config before it repopulates its live session table.
        let reloaded = Config::load(&config_path).await.unwrap();
        let manager = crate::session::test_manager(reloaded);
        manager.sync_from_config().await;
        let child = manager
            .views()
            .await
            .into_iter()
            .find(|session| session.name == "caller-worker")
            .expect("worker restored after restart");

        assert!(child.worker);
        assert!(child.durable);
        assert_eq!(child.parent, "caller");
        assert_eq!(child.task_id, "task-7");
        let _ = std::fs::remove_dir_all(dir);
    }

    #[test]
    fn worker_task_uses_caller_as_parent_metadata() {
        let dir = std::env::temp_dir().join(format!(
            "slopd-worker-attribution-{}-{}",
            std::process::id(),
            uuid::Uuid::new_v4()
        ));
        std::fs::create_dir_all(&dir).unwrap();
        let config = dir.join("config.toml");
        let mut tasks = crate::tasks::Tasks::load(&config).unwrap();
        let task = tasks
            .create_worker(
                "caller".into(),
                "caller-worker".into(),
                "inspect the build".into(),
                "caller".into(),
                false,
            )
            .unwrap();

        assert_eq!(task.from, "caller");
        assert_eq!(task.to, "caller-worker");
        let worker = task.worker.unwrap();
        assert_eq!(worker.parent, "caller");
        assert!(!worker.durable);
        let _ = std::fs::remove_dir_all(dir);
    }

    #[test]
    fn worker_does_not_duplicate_the_api_sandbox() {
        let parent = SessionCfg {
            sandbox: vec![WORKER_SANDBOX.into()],
            ..Default::default()
        };
        let child = clone_worker_session(parent, "child".into(), "parent");
        assert_eq!(child.sandbox, vec![WORKER_SANDBOX]);
    }
}
