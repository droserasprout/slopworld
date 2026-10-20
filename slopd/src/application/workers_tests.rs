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

#[tokio::test]
async fn allocation_failure_returns_inside_the_worker_request_boundary() {
    let manager = crate::session::test_manager(crate::config::Config::default());
    let result = tokio::time::timeout(
        std::time::Duration::from_secs(2),
        manager.session_request(NewWorktree::allocate(
            manager.clone(),
            Worktrees::new(&crate::application::Application::from_runtime(&manager)),
            crate::application::worktrees::WorktreeRequest {
                project: "missing".into(),
                ..Default::default()
            },
        )),
    )
    .await
    .expect("allocation must not wait for its own request boundary");
    let error = result
        .err()
        .expect("missing project must reject allocation");
    assert!(error.to_string().contains("no project missing"));
}

#[tokio::test]
async fn cancelled_worker_request_cleans_up_its_unattached_new_worktree() {
    let project_id = crate::storage_id::draft_identity();
    let manager = crate::session::test_manager(crate::config::Config {
        projects: vec![crate::config::ProjectCfg {
            id: project_id.clone(),
            name: "repo".into(),
            dir: std::env::temp_dir().to_string_lossy().into_owned(),
            ..Default::default()
        }],
        ..Default::default()
    });
    manager
        .save_worktrees(&crate::worktrees::Store {
            worktrees: vec![crate::worktrees::Worktree {
                id: "1111111111111111".into(),
                name: "new-tree".into(),
                path: "/tmp/new-tree".into(),
                repository: "/tmp/.git".into(),
                project_id,
                phase: crate::worktrees::WorktreePhase::Ready,
                ..Default::default()
            }],
        })
        .await
        .unwrap();
    drop(NewWorktree {
        worktrees: Worktrees::new(&crate::application::Application::from_runtime(&manager)),
        project: "repo".into(),
        id: Some("1111111111111111".into()),
    });
    tokio::time::timeout(std::time::Duration::from_secs(2), async {
        loop {
            if manager.worktree_records().worktrees.is_empty() {
                break;
            }
            tokio::task::yield_now().await;
        }
    })
    .await
    .expect("canceled request left an unattached worktree registered");
}
pub(crate) fn allocation_fixture() -> (Arc<SessionRuntime>, std::path::PathBuf, std::path::PathBuf)
{
    let root = std::env::temp_dir().join(format!("slopd-regression-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&root).unwrap();
    let main = root.join("repo");
    std::fs::create_dir(&main).unwrap();
    for args in [
        vec!["init", "-q"],
        vec![
            "-c",
            "user.name=Test",
            "-c",
            "user.email=test@example.invalid",
            "commit",
            "-q",
            "--allow-empty",
            "-m",
            "initial",
        ],
    ] {
        assert!(
            std::process::Command::new("git")
                .arg("-C")
                .arg(&main)
                .args(args)
                .status()
                .unwrap()
                .success()
        );
    }
    let project = crate::config::ProjectCfg {
        id: crate::storage_id::draft_identity(),
        name: "repo".into(),
        dir: main.to_string_lossy().into(),
        ..Default::default()
    };
    let manager = crate::session::test_manager(crate::config::Config {
        projects: vec![project],
        ..Default::default()
    });
    (manager, root, main)
}

#[tokio::test]
async fn allocation_and_rollback_complete_inside_the_worker_request_boundary() {
    let (manager, root, _) = allocation_fixture();
    tokio::time::timeout(
        std::time::Duration::from_secs(10),
        manager.session_request(async {
            let (id, mut owned) = NewWorktree::allocate(
                manager.clone(),
                Worktrees::new(&crate::application::Application::from_runtime(&manager)),
                crate::application::worktrees::WorktreeRequest {
                    project: "repo".into(),
                    name: "allocated".into(),
                    ..Default::default()
                },
            )
            .await
            .unwrap();
            let worktrees = manager.worktree_records();
            assert_eq!(worktrees.worktrees.len(), 1);
            let checkout = &worktrees.worktrees[0];
            assert_eq!(checkout.id, id);
            assert!(std::path::Path::new(&checkout.path).is_dir());
            owned.rollback().await;
            assert!(manager.worktree_records().worktrees.is_empty());
            assert!(!std::path::Path::new(&checkout.path).exists());
        }),
    )
    .await
    .expect("allocation must inherit the worker request boundary");
    std::fs::remove_dir_all(root).unwrap();
}

#[tokio::test]
async fn cancellation_during_allocation_rolls_back_checkout_and_registration() {
    let (manager, root, main) = allocation_fixture();
    let checkout =
        crate::worktrees::project_root(&manager.config().await.projects[0]).join("canceled");
    let mut events = manager.application.subscribe();
    manager
        .session_request(async {
            let allocation = NewWorktree::allocate(
                manager.clone(),
                Worktrees::new(&crate::application::Application::from_runtime(&manager)),
                crate::application::worktrees::WorktreeRequest {
                    project: "repo".into(),
                    name: "canceled".into(),
                    ..Default::default()
                },
            );
            let mut allocation = Box::pin(allocation);
            assert!(futures::poll!(allocation.as_mut()).is_pending());
            // The owned task has inherited this request's boundary. Cancel before yielding
            // to it so allocation and rollback must finish after the handler disappears.
            drop(allocation);
        })
        .await;
    tokio::time::timeout(std::time::Duration::from_secs(10), async {
        // Creation publishes a project snapshot before dropping its abandoned result.
        loop {
            if matches!(
                events.recv().await.unwrap().event(),
                crate::application::Event::Projects { .. }
            ) {
                break;
            }
        }
        while !manager.worktree_records().worktrees.is_empty() {
            tokio::task::yield_now().await;
        }
    })
    .await
    .expect("abandoned allocation must finish rollback");
    assert!(!checkout.exists());
    let listed = crate::worktrees::git(&main, &["worktree", "list", "--porcelain"])
        .await
        .unwrap();
    assert!(!listed.contains(checkout.to_str().unwrap()));
    std::fs::remove_dir_all(root).unwrap();
}
