use super::*;
use crate::agent_templates::AgentTemplate;
use crate::application::workers::Workers;
use crate::application::workers::tests::allocation_fixture;
use crate::application::workers::{WorkerCheckout, WorkerRequest, worker_session_from_template};
use crate::application::worktrees::WorktreeRequest;
use crate::application::worktrees::Worktrees;
use crate::config::{Config, Daemon, Limits, ProjectCfg, SessionCfg};
use std::collections::BTreeSet;
use std::path::PathBuf;

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
    manager
        .application
        .templates
        .store_for_test()
        .write()
        .await
        .templates = vec![template("review"), template("team-review")];

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
        .application
        .templates
        .store_for_test()
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

    manager
        .config_authority
        .write()
        .await
        .daemon
        .worker_templates
        .clear();
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
    assert_eq!(
        Workers::new(&crate::application::Application::from_runtime(&manager))
            .worker_project("caller", "")
            .await
            .unwrap(),
        "repo"
    );
    let error = Workers::new(&crate::application::Application::from_runtime(&manager))
        .worker_project("caller", "other")
        .await
        .unwrap_err();
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

    let error = Workers::new(&crate::application::Application::from_runtime(&manager))
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
    assert!(
        manager
            .task_store()
            .all_tasks_async()
            .await
            .unwrap()
            .is_empty()
    );
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

    let mut config = Config {
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
    config.daemon.token = "test-root-token".into();
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

#[tokio::test]
async fn invalid_worker_callers_and_projects_leave_no_tasks_or_sessions() {
    let manager = crate::session::test_manager(Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: "/tmp".into(),
            ..Default::default()
        }],
        sessions: vec![SessionCfg {
            name: "orphan".into(),
            ..Default::default()
        }],
        ..Default::default()
    });
    for (caller, project, message) in [
        (" ", "repo", "caller identity"),
        ("missing", "repo", "no such caller session"),
        (crate::tasks::HOST, "", "project context"),
        (crate::tasks::HOST, "missing", "no such worker project"),
        ("orphan", "repo", "no project context"),
    ] {
        let error = Workers::new(&crate::application::Application::from_runtime(&manager))
            .spawn_worker(
                caller.into(),
                project.into(),
                "review".into(),
                "work".into(),
                false,
            )
            .await
            .unwrap_err();
        assert!(error.to_string().contains(message), "{error:#}");
        assert!(
            manager
                .task_store()
                .all_tasks_async()
                .await
                .unwrap()
                .is_empty()
        );
        assert!(manager.live.read().await.is_empty());
        assert_eq!(manager.config().await.sessions.len(), 1);
    }
}

#[tokio::test]
async fn worker_name_allocation_skips_saved_agents_and_host_tabs() {
    let manager = crate::session::test_manager(Config {
        sessions: vec![SessionCfg {
            name: "caller-worker".into(),
            ..Default::default()
        }],
        host_terminals: vec![crate::config::HostTerminalCfg {
            name: "caller-worker-2".into(),
            ..Default::default()
        }],
        ..Default::default()
    });
    assert_eq!(manager.fresh_worker_name("caller").await, "caller-worker-3");
}

#[tokio::test]
async fn one_shot_registration_is_live_only_and_keeps_worker_identity() {
    let manager = crate::session::test_manager(Config::default());
    let mut worker =
        worker_session_from_template(&template("review"), "child".into(), "repo", "parent");
    worker.task_id = "task-1".into();
    manager.register_temporary_worker(&worker).await;
    assert!(manager.config().await.sessions.is_empty());
    let rows = manager.live.read().await;
    let row = &rows["child"];
    assert!(!row.role.is_durable_agent());
    assert_eq!(row.state, State::Down);
    assert_eq!(row.cfg.state_id, worker.state_id);
    assert_eq!(row.cfg.task_id, "task-1");
    assert_eq!(row.cfg.parent, "parent");
}

#[tokio::test]
async fn unusable_worker_templates_fail_before_creating_mailbox_records() {
    for (network, command, message) in [
        (NetworkMode::None, "echo ready", "disables networking"),
        (NetworkMode::Private, " ", "executable command"),
    ] {
        let manager = crate::session::test_manager(Config {
            projects: vec![ProjectCfg {
                name: "repo".into(),
                dir: "/tmp".into(),
                ..Default::default()
            }],
            ..Default::default()
        });
        let mut selected = template("review");
        selected.defaults.network = Some(network);
        selected.defaults.cmd = Some(command.into());
        // Blank overrides select the default preset. An empty captured command
        // exercises the unresolved-command rejection without launching a process.
        if command.trim().is_empty() {
            selected.defaults.command = Some(
                serde_json::from_value(serde_json::json!({
                    "name": "empty", "kind": "agent", "cmd": "", "sandbox": []
                }))
                .unwrap(),
            );
        }
        manager
            .application
            .templates
            .store_for_test()
            .write()
            .await
            .templates = vec![selected];
        let error = Workers::new(&crate::application::Application::from_runtime(&manager))
            .spawn_worker(
                crate::tasks::HOST.into(),
                "repo".into(),
                "review".into(),
                "work".into(),
                false,
            )
            .await
            .unwrap_err();
        assert!(error.to_string().contains(message), "{error:#}");
        assert!(
            manager
                .task_store()
                .all_tasks_async()
                .await
                .unwrap()
                .is_empty()
        );
        assert!(manager.config().await.sessions.is_empty());
        assert!(manager.live.read().await.is_empty());
    }
}

#[tokio::test]
async fn new_checkout_preflight_rejects_invalid_workers_without_allocating() {
    let (manager, root, main) = allocation_fixture();
    let mut selected = template("review");
    selected.defaults.network = Some(NetworkMode::None);
    manager
        .application
        .templates
        .store_for_test()
        .write()
        .await
        .templates = vec![selected];
    for (body, selected, expected) in [
        (" ", "review", "task text cannot be empty"),
        ("work", "missing", "no such agent template"),
        ("work", "review", "disables networking"),
    ] {
        let error = Workers::new(&crate::application::Application::from_runtime(&manager))
            .create_worker(WorkerRequest {
                caller: crate::tasks::HOST.into(),
                project: "repo".into(),
                template: selected.into(),
                body: body.into(),
                durable: false,
                checkout: WorkerCheckout::New {
                    base: String::new(),
                    name: "unallocated".into(),
                },
            })
            .await
            .unwrap_err();
        assert!(error.to_string().contains(expected), "{error:#}");
        assert!(manager.worktree_records().worktrees.is_empty());
        assert!(
            manager
                .task_store()
                .all_tasks_async()
                .await
                .unwrap()
                .is_empty()
        );
        assert!(manager.config().await.sessions.is_empty());
        assert!(manager.live.read().await.is_empty());
    }
    let listed = crate::worktrees::git(&main, &["worktree", "list", "--porcelain"])
        .await
        .unwrap();
    assert!(!listed.contains("unallocated"));
    std::fs::remove_dir_all(root).unwrap();
}

use crate::test_support::isolated_worker_runtime;

async fn worker_runtime_fixture() -> (Arc<SessionRuntime>, PathBuf) {
    let (manager, root, _) = allocation_fixture();
    let mut selected = template("review");
    selected.defaults.network = Some(NetworkMode::Host);
    selected.defaults.limits = Limits::default();
    selected.defaults.cmd = Some("/bin/sleep 60".into());
    selected.defaults.persistent_tmp = Some(false);
    manager
        .application
        .templates
        .store_for_test()
        .write()
        .await
        .templates = vec![selected];
    manager
        .config_authority
        .write()
        .await
        .daemon
        .instructions
        .worker_prompt
        .clear();
    (manager, root)
}

fn worker_request(durable: bool, checkout: WorkerCheckout) -> WorkerRequest {
    WorkerRequest {
        caller: crate::tasks::HOST.into(),
        project: "repo".into(),
        template: "review".into(),
        body: "inspect the build".into(),
        durable,
        checkout,
    }
}

fn new_worker_checkout() -> WorkerCheckout {
    WorkerCheckout::New {
        base: String::new(),
        name: "worker-checkout".into(),
    }
}

#[tokio::test]
async fn worker_creation_commits_task_session_and_checkout_before_returning() {
    let Some(_) = isolated_worker_runtime() else {
        return;
    };
    for durable in [false, true] {
        let (manager, root) = worker_runtime_fixture().await;
        let existing = Worktrees::new(&crate::application::Application::from_runtime(&manager))
            .create_worktree(WorktreeRequest {
                project: "repo".into(),
                name: "existing".into(),
                ..Default::default()
            })
            .await
            .unwrap();
        for checkout in [
            WorkerCheckout::Existing(existing.id.clone()),
            new_worker_checkout(),
        ] {
            let spawned = Workers::new(&crate::application::Application::from_runtime(&manager))
                .create_worker(worker_request(durable, checkout))
                .await
                .unwrap();
            let tasks = manager.task_store().all_tasks_async().await.unwrap();
            let task = tasks
                .iter()
                .find(|task| task.id == spawned.task.id)
                .unwrap();
            assert_eq!(task.status, crate::tasks::Status::Queued);
            assert_eq!(task.body, "inspect the build");
            assert_eq!(task.worker.as_ref().unwrap().durable, durable);
            assert_eq!(task.worker.as_ref().unwrap().parent, crate::tasks::HOST);
            let session = manager.session_cfg(&spawned.session).await.unwrap();
            assert_eq!(session.task_id, task.id);
            assert!(session.worker);
            assert!(!session.autostart && !session.auto_resume);
            assert!(!session.state_id.is_empty());
            assert!(manager.tmux.exists(&spawned.session).await);
            assert!(
                manager.live.read().await[&spawned.session]
                    .capture
                    .reader
                    .is_some()
            );
            let persisted = Config::load(manager.cfg_path()).await.unwrap();
            assert_eq!(persisted.session(&spawned.session).is_some(), durable);
            if durable {
                assert_eq!(
                    persisted.session(&spawned.session).unwrap().state_id,
                    session.state_id
                );
            }
            let records = manager.worktree_records();
            let selected = records
                .worktrees
                .iter()
                .find(|tree| tree.id == session.worktree)
                .unwrap();
            assert!(Path::new(&selected.path).is_dir());
            manager.stop(&spawned.session).await.unwrap();
            assert!(
                manager
                    .worktree_records()
                    .worktrees
                    .iter()
                    .any(|tree| tree.id == selected.id)
            );
            assert!(
                Path::new(&selected.path).is_dir(),
                "worker cleanup must preserve checkout"
            );
        }
        assert_eq!(manager.worktree_records().worktrees.len(), 2);
        std::fs::remove_dir_all(root).unwrap();
    }
}

#[tokio::test]
async fn worker_start_failure_retains_only_durable_session_and_its_new_checkout() {
    let Some(_) = isolated_worker_runtime() else {
        return;
    };
    for durable in [false, true] {
        let (manager, root) = worker_runtime_fixture().await;
        let reached = Arc::new(tokio::sync::Notify::new());
        let release = Arc::new(tokio::sync::Notify::new());
        *manager.reader_attach_pause.lock().unwrap() = Some((reached.clone(), release.clone()));
        let owner = manager.clone();
        let operation = tokio::spawn(async move {
            Workers::new(&crate::application::Application::from_runtime(&owner))
                .create_worker(worker_request(durable, new_worker_checkout()))
                .await
        });
        tokio::time::timeout(Duration::from_secs(10), reached.notified())
            .await
            .unwrap();
        let task = manager
            .task_store()
            .all_tasks_async()
            .await
            .unwrap()
            .pop()
            .unwrap();
        assert_eq!(task.status, crate::tasks::Status::Queued);
        let checkout = manager.worktree_records().worktrees[0].clone();
        manager.tmux.kill(&task.to).await.unwrap();
        release.notify_one();
        let error = tokio::time::timeout(Duration::from_secs(10), operation)
            .await
            .unwrap()
            .unwrap()
            .unwrap_err();
        assert!(format!("{error:#}").contains("reader"), "{error:#}");
        let task = manager
            .task_store()
            .all_tasks_async()
            .await
            .unwrap()
            .pop()
            .unwrap();
        assert_eq!(task.status, crate::tasks::Status::Failed);
        assert_eq!(manager.config().await.session(&task.to).is_some(), durable);
        assert_eq!(manager.live.read().await.contains_key(&task.to), durable);
        assert_eq!(
            manager.worktree_records().worktrees.len(),
            usize::from(durable)
        );
        assert_eq!(Path::new(&checkout.path).exists(), durable);
        assert!(!manager.tmux.exists(&task.to).await);
        std::fs::remove_dir_all(root).unwrap();
    }
}

#[tokio::test]
async fn cancelled_worker_start_finishes_commit_and_preserves_attached_checkout() {
    let Some(_) = isolated_worker_runtime() else {
        return;
    };
    let (manager, root) = worker_runtime_fixture().await;
    let reached = Arc::new(tokio::sync::Notify::new());
    let release = Arc::new(tokio::sync::Notify::new());
    *manager.reader_attach_pause.lock().unwrap() = Some((reached.clone(), release.clone()));
    let owner = manager.clone();
    let operation = tokio::spawn(async move {
        Workers::new(&crate::application::Application::from_runtime(&owner))
            .create_worker(worker_request(true, new_worker_checkout()))
            .await
    });
    tokio::time::timeout(Duration::from_secs(10), reached.notified())
        .await
        .unwrap();
    operation.abort();
    assert!(operation.await.unwrap_err().is_cancelled());
    manager.session_boundary.try_write().unwrap_err();
    release.notify_one();
    tokio::time::timeout(Duration::from_secs(10), manager.session_operation(async {}))
        .await
        .unwrap();
    let task = manager
        .task_store()
        .all_tasks_async()
        .await
        .unwrap()
        .pop()
        .unwrap();
    assert_eq!(task.status, crate::tasks::Status::Queued);
    assert!(manager.tmux.exists(&task.to).await);
    let checkout = manager.worktree_records().worktrees[0].clone();
    assert!(Path::new(&checkout.path).is_dir());
    assert_eq!(
        manager.config().await.session(&task.to).unwrap().worktree,
        checkout.id
    );
    manager.stop(&task.to).await.unwrap();
    std::fs::remove_dir_all(root).unwrap();
}

#[tokio::test]
async fn worker_registration_failure_fails_task_and_rolls_back_new_checkout() {
    let Some(_) = isolated_worker_runtime() else {
        return;
    };
    let (manager, root) = worker_runtime_fixture().await;
    // Pause after allocation but before task/agent publication, while preserving
    // the coordinator's accepted configuration and request context.
    let spawn_guard = manager.worker_spawn.lock().await;
    let mut events = manager.application.subscribe();
    let owner = manager.clone();
    let operation = tokio::spawn(async move {
        Workers::new(&crate::application::Application::from_runtime(&owner))
            .create_worker(worker_request(true, new_worker_checkout()))
            .await
    });
    tokio::time::timeout(Duration::from_secs(10), async {
        loop {
            if matches!(events.recv().await.unwrap().event(), Event::Projects { .. }) {
                break;
            }
        }
    })
    .await
    .unwrap();
    // Fail publication even as root, while still permitting identity inventory.
    let agents = manager
        .workspace
        .configuration
        .records
        .binding
        .data
        .join("agents");
    let fault = crate::paths::fail_writes(&agents);
    drop(spawn_guard);
    let error = tokio::time::timeout(Duration::from_secs(10), operation)
        .await
        .unwrap()
        .unwrap()
        .unwrap_err();
    assert!(format!("{error:#}").contains("injected persistence failure"));
    let task = manager
        .task_store()
        .all_tasks_async()
        .await
        .unwrap()
        .pop()
        .unwrap();
    assert_eq!(task.status, crate::tasks::Status::Failed);
    assert!(
        task.note
            .as_deref()
            .unwrap()
            .contains("could not persist worker session")
    );
    assert!(manager.config().await.sessions.is_empty());
    assert!(manager.live.read().await.is_empty());
    assert!(manager.worktree_records().worktrees.is_empty());
    let checkout =
        crate::worktrees::project_root(&manager.config().await.projects[0]).join("worker-checkout");
    assert!(!checkout.exists());
    drop(fault);
    std::fs::remove_dir_all(root).unwrap();
}
