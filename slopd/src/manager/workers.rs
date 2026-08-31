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
        project: String,
        template: String,
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
        if parent != crate::tasks::HOST && !self.session_known(&parent).await {
            bail!("no such parent session: {parent}");
        }
        if parent != crate::tasks::HOST && self.is_host(&parent).await {
            bail!("a host terminal cannot own a worker; choose an agent parent");
        }

        let project = if project.trim().is_empty() {
            if parent == crate::tasks::HOST {
                bail!("a host-owned worker needs a project");
            }
            self.session_cfg(&parent)
                .await
                .map(|s| s.project)
                .filter(|p| !p.trim().is_empty())
                .ok_or_else(|| anyhow!("parent {parent} has no project"))?
        } else {
            project.trim().to_string()
        };
        let p = cfg
            .project(&project)
            .cloned()
            .ok_or_else(|| anyhow!("no such worker project: {project}"))?;

        let template = if template.trim().is_empty() {
            cfg.defaults.agent.trim().to_string()
        } else {
            template.trim().to_string()
        };
        let presets = crate::presets::table();
        let command = presets
            .command(&template)
            .ok_or_else(|| anyhow!("unknown worker command template: {template}"))?;
        if command.cmd.trim().is_empty() {
            bail!("worker command template {template} has no command");
        }

        let mut session = SessionCfg {
            name: self.fresh_worker_name(&parent).await,
            project: project.clone(),
            command: template,
            sandbox: vec![WORKER_SANDBOX.into()],
            worker: true,
            parent: parent.clone(),
            ..Default::default()
        };
        // A worker must be able to reach the daemon and must carry the exact API-capability
        // preset. The preset is deliberately appended to the requested template rather than
        // replacing its normal tool sandbox.
        if cfg.network_of(&session, &p) == NetworkMode::None {
            bail!("worker project {project} disables networking; task API access needs a network");
        }
        let table = crate::presets::table();
        crate::sandbox::validate_preset_name(WORKER_SANDBOX, &table)
            .context("worker task API preset is invalid")?;
        if cfg.command_of(&session).trim().is_empty() {
            bail!(
                "worker command template does not resolve to an executable: {}",
                session.command
            );
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
            let mut cfg = self.cfg.write().await;
            if cfg.session(&session.name).is_some()
                || cfg
                    .host_terminals
                    .iter()
                    .any(|tab| tab.name == session.name)
            {
                self.fail_worker_task(&task.id, "worker name became unavailable");
                bail!("worker name became unavailable: {}", session.name);
            }
            cfg.sessions.push(session.clone());
            if let Err(error) = self.save_cfg(&cfg) {
                self.fail_worker_task(
                    &task.id,
                    format!("could not persist worker session: {error:#}"),
                );
                return Err(error);
            }
            drop(cfg);
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
        let base = if parent == crate::tasks::HOST {
            "worker".to_string()
        } else {
            format!("{parent}-worker")
        };
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

#[cfg(test)]
mod tests {
    use super::*;
    use crate::config::{Config, Daemon, ProjectCfg, SessionCfg};

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
}
