//! Task-owned worker creation. A worker is a normal sandboxed session with explicit ownership
//! metadata and a durable mailbox task; its name is never used to reconstruct that relationship.

use super::super::*;

use anyhow::anyhow;

pub(crate) const WORKER_SANDBOX: &str = "slopworld-worker";
pub(crate) const WORKER_BOOTSTRAP: &str = "You are a SlopWorld worker. Your assigned task is $SLOPWORLD_TASK_ID. Run slopctl task with that exact ID, accept it, then complete it. Do not duplicate the task body into the prompt and do not rely on an ambiguous inbox search.";

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
        parent: String,
        body: String,
        durable: bool,
    ) -> Result<WorkerSpawn> {
        let _spawn = self.worker_spawn.lock().await;
        self.reload_if_changed().await;
        let cfg = self.config().await;
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

        let mut session = clone_worker_session(
            parent_session,
            self.fresh_worker_name(&parent).await,
            &parent,
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

        let task = self.tasks.lock().unwrap().create_worker(
            parent.clone(),
            session.name.clone(),
            body,
            parent.clone(),
            durable,
        )?;
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

        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });
        let manager = self.clone();
        let name = session.name.clone();
        tokio::spawn(async move { manager.deliver(&name, WORKER_BOOTSTRAP, Vec::new()).await });
        Ok(WorkerSpawn {
            task,
            session: session.name,
        })
    }

    async fn fresh_worker_name(&self, parent: &str) -> String {
        let base = format!("{parent}-worker");
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
/// networking, limits, mounts, and lifecycle settings all remain the parent's choices.
fn clone_worker_session(mut parent: SessionCfg, name: String, parent_name: &str) -> SessionCfg {
    parent.name = name;
    parent.state_id = uuid::Uuid::new_v4().to_string();
    parent.worker = true;
    parent.parent = parent_name.to_string();
    parent.task_id.clear();
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
        };
        let child = clone_worker_session(parent.clone(), "parent-worker".into(), "parent");

        assert_eq!(child.name, "parent-worker");
        assert_ne!(child.state_id, parent.state_id);
        assert!(child.worker);
        assert_eq!(child.parent, "parent");
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
        assert_eq!(child.autostart, parent.autostart);
        assert_eq!(child.auto_resume, parent.auto_resume);
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
