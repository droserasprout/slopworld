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
fn worker_policy_round_trips_exact_template_names() {
    let mut config = Config::default();
    config.daemon.worker_templates =
        BTreeSet::from(["review".to_string(), "team-review".to_string()]);
    let loaded: Config = toml::from_str(&toml::to_string(&config).unwrap()).unwrap();
    assert_eq!(
        loaded.daemon.worker_templates,
        config.daemon.worker_templates
    );
}

#[tokio::test]
async fn worker_policy_keeps_template_names_distinct() {
    let manager = crate::session::test_manager(Config {
        settings: crate::config::Settings {
            daemon: Daemon {
                worker_templates: BTreeSet::from(["team-review".to_string()]),
                ..Default::default()
            },
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
    manager.templates.store.write().await.templates =
        vec![template("review"), template("team-review")];

    assert_eq!(
        manager
            .spawnable_worker_template(crate::tasks::HOST, "repo", "team-review")
            .await
            .unwrap()
            .name,
        "team-review"
    );
    assert_eq!(
        manager
            .spawnable_worker_template("caller", "repo", "team-review")
            .await
            .unwrap()
            .name,
        "team-review"
    );
    let error = manager
        .spawnable_worker_template("caller", "repo", "review")
        .await
        .unwrap_err()
        .to_string();
    assert!(error.contains("Worker policy does not allow"), "{error}");

    manager
        .templates
        .store
        .write()
        .await
        .templates
        .retain(|template| template.name != "team-review");
    let error = manager
        .spawnable_worker_template(crate::tasks::HOST, "repo", "team-review")
        .await
        .unwrap_err()
        .to_string();
    assert!(error.contains("no such agent template"), "{error}");

    manager.cfg.write().await.daemon.worker_templates.clear();
    assert_eq!(
        manager
            .spawnable_worker_template(crate::tasks::HOST, "repo", "review")
            .await
            .unwrap()
            .name,
        "review"
    );
    let error = manager
        .spawnable_worker_template("caller", "repo", "review")
        .await
        .unwrap_err()
        .to_string();
    assert!(error.contains("Worker policy does not allow"), "{error}");
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
        settings: crate::config::Settings {
            daemon: Daemon::default(),
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
        settings: crate::config::Settings {
            daemon,
            ..Default::default()
        },
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
    let child = worker_session_from_template(&template, "caller-worker".into(), "repo", "caller");

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
                "command": {"name": "codex", "kind": "agent", "cmd": "codex --full-auto", "sandbox": []},
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
    crate::config::fixtures::save(&config, &config_path)
        .await
        .unwrap();

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

    assert!(child.worker.enabled);
    assert!(child.worker.durable);
    assert_eq!(child.worker.parent, "caller");
    assert_eq!(child.worker.task_id, "task-7");
    drop(std::fs::remove_dir_all(dir));
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
    drop(std::fs::remove_dir_all(dir));
}

#[test]
fn worker_inherits_extra_arguments_from_selected_template() {
    let mut selected = template("reviewer");
    selected.defaults.args = Some("--model 'two words'".into());
    let worker = worker_session_from_template(&selected, "worker".into(), "repo", "caller");
    assert_eq!(worker.args, selected.defaults.args);
    assert_eq!(
        Config::default().command_of(&worker),
        "codex --full-auto --model 'two words'"
    );
}
