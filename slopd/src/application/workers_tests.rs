use super::*;
use crate::config::{DnsConfig, Limits};
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

#[test]
fn project_policy_rejects_missing_host_and_unscoped_callers() {
    let cfg = Config {
        projects: vec![crate::config::ProjectCfg {
            name: "repo".into(),
            ..Default::default()
        }],
        ..Default::default()
    };
    for (context, requested, expected) in [
        (Caller::Missing, "repo", "no such caller"),
        (Caller::HostTerminal, "repo", "host terminal"),
        (
            Caller::Agent {
                project: " ".into(),
            },
            "repo",
            "no project context",
        ),
        (Caller::Root, "", "needs a project context"),
        (Caller::Root, "absent", "no such worker project"),
        (
            Caller::Agent {
                project: "other".into(),
            },
            "repo",
            "only in project other",
        ),
    ] {
        let error = project(&cfg, "caller", &context, requested).unwrap_err();
        assert!(error.to_string().contains(expected), "{error:#}");
    }
    assert_eq!(
        project(
            &cfg,
            "caller",
            &Caller::Agent {
                project: " repo ".into()
            },
            " "
        )
        .unwrap(),
        "repo"
    );
}

#[test]
fn template_policy_checks_identity_scope_and_exact_allowlist_before_lookup() {
    let mut cfg = Config::default();
    cfg.daemon.worker_templates.insert("review".into());
    let templates = vec![template("review"), template("team-review")];
    for (context, requested, expected) in [
        (Caller::Root, "", "needs a template"),
        (Caller::Missing, "review", "no such caller"),
        (Caller::HostTerminal, "review", "host terminal"),
        (
            Caller::Agent {
                project: "other".into(),
            },
            "review",
            "cannot use project",
        ),
        (
            Caller::Agent {
                project: "repo".into(),
            },
            "team-review",
            "does not allow",
        ),
        (Caller::Root, "deleted", "no such agent template"),
    ] {
        let error =
            super::template(&cfg, "caller", &context, "repo", requested, &templates).unwrap_err();
        assert!(error.to_string().contains(expected), "{error:#}");
    }
    assert_eq!(
        super::template(
            &cfg,
            "caller",
            &Caller::Root,
            "repo",
            "team-review",
            &templates
        )
        .unwrap()
        .name,
        "team-review"
    );
}
