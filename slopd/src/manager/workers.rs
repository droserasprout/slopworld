//! Task-owned worker creation. A worker is a normal sandboxed session with explicit ownership
//! metadata and a durable mailbox task; its name is never used to reconstruct that relationship.

use super::super::*;

use anyhow::anyhow;

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
        project: String,
        template: String,
        body: String,
        durable: bool,
    ) -> Result<WorkerSpawn> {
        self.session_operation(
            self.spawn_worker_within_boundary(caller, project, template, body, durable),
        )
        .await
    }

    async fn spawn_worker_within_boundary(
        self: &Arc<Self>,
        caller: String,
        project: String,
        template_name: String,
        body: String,
        durable: bool,
    ) -> Result<WorkerSpawn> {
        let _spawn = self.worker_spawn.lock().await;
        self.reload_if_changed().await;
        let caller = caller.trim().to_string();
        if caller.is_empty() {
            bail!("a worker needs a caller identity");
        }
        if caller != crate::tasks::HOST && !self.session_known(&caller).await {
            bail!("no such caller session: {caller}");
        }
        let project = self.worker_project(&caller, project.trim()).await?;
        let template = self
            .spawnable_worker_template(&caller, &project, template_name.trim())
            .await?;
        let cfg = self.config().await;
        let p = cfg
            .project(&project)
            .cloned()
            .ok_or_else(|| anyhow!("no such worker project: {project}"))?;

        // The selected template supplies behavior; the invoking session owns the child in the
        // task mailbox and sidebar. No caller configuration is copied into the worker.
        let mut session = worker_session_from_template(
            &template,
            self.fresh_worker_name(&caller).await,
            &project,
            &caller,
        );
        // A worker must be able to reach the daemon. Worker metadata supplies its task API
        // capability; the selected template supplies the normal tool and state configuration.
        if cfg.network_of(&session, &p) == NetworkMode::None {
            bail!(
                "worker template {} disables networking; task API access needs a network",
                template.name
            );
        }
        if cfg.command_of(&session).trim().is_empty() {
            bail!(
                "worker template {} does not resolve to an executable command",
                template.name
            );
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

        self.announce_sessions().await;
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

/// Validate the project context a worker caller is allowed to use. Root callers may choose any
/// registered project; an agent may only create a worker in its own project. Keeping this check
/// here as well as in the HTTP handler protects direct manager callers and future transports.
impl Manager {
    pub(crate) async fn worker_project(&self, caller: &str, requested: &str) -> Result<String> {
        let requested = requested.trim();
        let cfg = self.config().await;
        let caller_project = if caller != crate::tasks::HOST {
            let session = self
                .session_cfg(caller)
                .await
                .ok_or_else(|| anyhow!("no such caller session: {caller}"))?;
            if self.is_host(caller).await {
                bail!("a host terminal cannot own a worker; choose an agent caller");
            }
            let project = session.project.trim();
            if project.is_empty() {
                bail!("caller {caller} has no project context");
            }
            Some(project.to_string())
        } else {
            None
        };
        let project = if requested.is_empty() {
            caller_project
                .clone()
                .ok_or_else(|| anyhow!("a worker needs a project context"))?
        } else {
            requested.to_string()
        };
        if cfg.project(&project).is_none() {
            bail!("no such worker project: {project}");
        }
        if let Some(caller_project) = caller_project {
            if caller_project != project {
                bail!("caller {caller} may spawn workers only in project {caller_project}");
            }
        }
        Ok(project.to_string())
    }

    pub(crate) async fn worker_template_names(&self) -> std::collections::BTreeSet<String> {
        self.config().await.daemon.worker_templates.clone()
    }

    /// Return the exact enabled definition from the live personal/repository catalog. Policy is
    /// checked by qualified identity before task/session allocation, so a deleted or unchecked
    /// template cannot leave a mailbox or private state behind.
    pub(crate) async fn spawnable_worker_template(
        &self,
        caller: &str,
        project: &str,
        requested: &str,
    ) -> Result<AgentTemplate> {
        if requested.is_empty() {
            bail!("a worker needs a template");
        }
        let cfg = self.config().await;
        if caller != crate::tasks::HOST {
            let session = self
                .session_cfg(caller)
                .await
                .ok_or_else(|| anyhow!("no such caller session: {caller}"))?;
            if session.project.trim() != project {
                bail!("caller {caller} is not authorized for project {project}");
            }
        }
        if !cfg.daemon.worker_templates.contains(requested) {
            bail!("agent template {requested} is not enabled for workers");
        }
        drop(cfg);
        self.agent_templates()
            .await
            .into_iter()
            .find(|template| template.name == requested)
            .ok_or_else(|| anyhow!("no such agent template: {requested}"))
    }
}

/// Instantiate a selected recipe with a new private identity and daemon-owned hierarchy
/// metadata. Workers use the selected project's live mounts and never inherit caller settings.
/// They are deliberately stopped after their task exits: retrying a task is an explicit operator
/// decision, not a daemon loop.
fn worker_session_from_template(
    template: &AgentTemplate,
    name: String,
    project: &str,
    owner_name: &str,
) -> SessionCfg {
    let mut session = template.instantiate(name, project.to_string());
    session.worker = true;
    session.parent = owner_name.to_string();
    session.task_id.clear();
    session.autostart = false;
    session.auto_resume = false;
    session.worker_token = None;
    session
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::config::{Config, Daemon, DnsConfig, Limits, ProjectCfg, SessionCfg};
    use std::collections::BTreeSet;

    fn template(name: &str) -> AgentTemplate {
        serde_json::from_value(serde_json::json!({
            "name": name,
            "defaults": {
                "cmd": "codex --full-auto",
                "sandbox": [],
                "sandbox_presets": [],
                "persistent_tmp": true,
                "network": "private",
                "dns": {"mode": "resolved"},
                "limits": {"memory_mb": 1024, "pids": 64, "nofile": 128, "cpu_pct": 75},
                "autostart": true,
                "auto_resume": true
            }
        }))
        .unwrap()
    }

    #[test]
    fn worker_policy_round_trips_exact_qualified_template_names() {
        let mut config = Config::default();
        config.daemon.worker_templates =
            BTreeSet::from(["review".to_string(), "repo::review".to_string()]);
        let loaded: Config = toml::from_str(&toml::to_string(&config).unwrap()).unwrap();
        assert_eq!(
            loaded.daemon.worker_templates,
            config.daemon.worker_templates
        );
    }

    #[tokio::test]
    async fn worker_policy_keeps_personal_and_repository_names_distinct() {
        let manager = crate::session::test_manager(Config {
            daemon: Daemon {
                worker_templates: BTreeSet::from(["repo::review".to_string()]),
                ..Default::default()
            },
            projects: vec![ProjectCfg {
                name: "repo".into(),
                dir: "/tmp".into(),
                ..Default::default()
            }],
            sessions: vec![SessionCfg {
                name: "caller".into(),
                project: "repo".into(),
                ..Default::default()
            }],
            ..Default::default()
        });
        manager.templates.write().await.templates =
            vec![template("review"), template("repo::review")];

        assert_eq!(
            manager
                .spawnable_worker_template(crate::tasks::HOST, "repo", "repo::review")
                .await
                .unwrap()
                .name,
            "repo::review"
        );
        let error = manager
            .spawnable_worker_template(crate::tasks::HOST, "repo", "review")
            .await
            .unwrap_err()
            .to_string();
        assert!(error.contains("not enabled for workers"), "{error}");

        manager
            .templates
            .write()
            .await
            .templates
            .retain(|template| template.name != "repo::review");
        let error = manager
            .spawnable_worker_template(crate::tasks::HOST, "repo", "repo::review")
            .await
            .unwrap_err()
            .to_string();
        assert!(error.contains("no such agent template"), "{error}");

        manager.cfg.write().await.daemon.worker_templates.clear();
        let error = manager
            .spawnable_worker_template(crate::tasks::HOST, "repo", "review")
            .await
            .unwrap_err()
            .to_string();
        assert!(error.contains("not enabled for workers"), "{error}");
    }

    #[tokio::test]
    async fn scoped_worker_callers_cannot_cross_project_contexts() {
        let manager = crate::session::test_manager(Config {
            projects: vec![
                ProjectCfg {
                    name: "repo".into(),
                    dir: "/tmp".into(),
                    ..Default::default()
                },
                ProjectCfg {
                    name: "other".into(),
                    dir: "/tmp".into(),
                    ..Default::default()
                },
            ],
            sessions: vec![SessionCfg {
                name: "caller".into(),
                project: "repo".into(),
                ..Default::default()
            }],
            ..Default::default()
        });
        assert_eq!(manager.worker_project("caller", "").await.unwrap(), "repo");
        let error = manager.worker_project("caller", "other").await.unwrap_err();
        let error = error.to_string();
        assert!(error.contains("only in project repo"), "{error}");
    }

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
                name: "caller".into(),
                project: "repo".into(),
                ..Default::default()
            }],
            ..Default::default()
        });
        assert_eq!(manager.fresh_worker_name("caller").await, "caller-worker");
        manager.live.write().await.insert(
            "caller-worker".into(),
            Live::new(SessionCfg::default(), TitleCapture::default()),
        );
        assert_eq!(manager.fresh_worker_name("caller").await, "caller-worker-2");

        // Names are derived from the task caller, never from the selected template.
        assert_eq!(manager.fresh_worker_name("other").await, "other-worker");
    }

    #[tokio::test]
    async fn invalid_worker_template_is_rejected_before_task_creation() {
        let mut daemon = Daemon::default();
        daemon.worker_templates.insert("missing".into());
        let manager = crate::session::test_manager(Config {
            daemon,
            projects: vec![ProjectCfg {
                name: "repo".into(),
                dir: "/tmp".into(),
                ..Default::default()
            }],
            ..Default::default()
        });

        let error = manager
            .spawn_worker(
                crate::tasks::HOST.into(),
                "repo".into(),
                "missing".into(),
                "inspect the build".into(),
                false,
            )
            .await
            .unwrap_err()
            .to_string();

        assert!(error.contains("no such agent template: missing"), "{error}");
        assert!(manager.tasks.all_tasks().is_empty());
    }

    #[test]
    fn worker_instantiates_template_and_only_replaces_child_metadata() {
        let template = template("worker");
        let child =
            worker_session_from_template(&template, "caller-worker".into(), "repo", "caller");

        assert_eq!(child.name, "caller-worker");
        assert!(!child.state_id.is_empty());
        assert!(child.worker);
        assert_eq!(child.parent, "caller");
        assert!(child.task_id.is_empty());
        assert_eq!(child.project, "repo");
        assert_eq!(child.cmd.as_deref(), Some("codex --full-auto"));
        assert!(child.sandbox.is_empty());
        assert!(child.persistent_tmp);
        assert_eq!(child.network, NetworkMode::Private);
        assert_eq!(child.dns, DnsConfig::Resolved);
        assert_eq!(
            child.limits,
            Limits {
                memory_mb: Some(1024),
                pids: Some(64),
                nofile: Some(128),
                cpu_pct: Some(75),
            }
        );
        assert!(!child.autostart);
        assert!(!child.auto_resume);
    }

    #[test]
    fn worker_template_keeps_snapshot_settings_independent_of_source_sessions() {
        let template: AgentTemplate = serde_json::from_value(serde_json::json!({
            "name": "worker",
            "version": 1,
            "defaults": {
                "command": {"name": "codex", "cmd": "codex --full-auto", "sandbox": []},
                "sandbox": ["captured"],
                "sandbox_presets": [{"name": "captured"}],
                "persistent_tmp": false,
                "network": "private",
                "dns": {"mode": "resolved"},
                "limits": {},
                "autostart": false,
                "auto_resume": false
            }
        }))
        .unwrap();
        let child = worker_session_from_template(&template, "child".into(), "repo", "caller");
        assert_eq!(child.command_snapshot.unwrap().name, "codex");
        assert_eq!(child.sandbox_snapshots[0].name, "captured");
        assert_eq!(child.sandbox, ["captured"]);
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
}
