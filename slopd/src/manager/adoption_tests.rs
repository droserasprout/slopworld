use super::recovered_worker_cfg;
use crate::config::{Config, SessionCfg};

#[test]
fn recovered_worker_uses_parent_settings_and_task_identity() {
    let cfg = Config {
        sessions: vec![SessionCfg {
            name: "parent".into(),
            project: "repo".into(),
            command: "codex".into(),
            sandbox: vec!["gpu".into()],
            ..Default::default()
        }],
        ..Default::default()
    };
    let worker = recovered_worker_cfg(
        &cfg,
        "parent-worker",
        &crate::tmux::WorkerMetadata {
            project: Some("repo".into()),
            worktree: Some("worker-worktree".into()),
            parent: "parent".into(),
            task_id: "task-7".into(),
            durable: false,
            state_id: Some("11111111-1111-4111-8111-111111111111".into()),
        },
    );

    assert!(worker.worker);
    assert_eq!(worker.parent, "parent");
    assert_eq!(worker.task_id, "task-7");
    assert_eq!(worker.state_id, "11111111-1111-4111-8111-111111111111");
    assert_eq!(worker.project, "repo");
    assert_eq!(worker.worktree, "worker-worktree");
    assert_eq!(worker.command, "codex");
    assert_eq!(worker.sandbox, ["gpu"]);
    assert!(!worker.autostart);
    assert!(!worker.auto_resume);
}
