use super::*;
use crate::application::workers::Workers;

#[tokio::test]
async fn worker_base_preview_uses_selected_callers_distinct_worktree_head() {
    use crate::application::worktrees::WorktreeRequest;
    use crate::worktrees::git;
    let (sessions, root, main) = crate::application::workers::tests::allocation_fixture();
    let manager = crate::application::Application::from_runtime(&sessions);
    let worktrees = Worktrees::new(&manager);
    let caller_tree = worktrees
        .create_worktree(WorktreeRequest {
            project: "repo".into(),
            name: "caller-tree".into(),
            ..Default::default()
        })
        .await
        .unwrap();
    let caller_path = std::path::Path::new(&caller_tree.path);
    assert!(
        std::process::Command::new("git")
            .arg("-C")
            .arg(caller_path)
            .args([
                "-c",
                "user.name=Test",
                "-c",
                "user.email=test@example.com",
                "commit",
                "--quiet",
                "--allow-empty",
                "-m",
                "caller commit",
            ])
            .status()
            .unwrap()
            .success()
    );
    let main_head = git(&main, &["rev-parse", "HEAD"]).await.unwrap();
    let caller_head = git(caller_path, &["rev-parse", "HEAD"]).await.unwrap();
    assert_ne!(main_head, caller_head);
    sessions
        .register_temporary_worker(&crate::config::SessionCfg {
            name: "caller".into(),
            project: "repo".into(),
            worktree: caller_tree.id,
            ..Default::default()
        })
        .await;

    for (caller, base, expected) in [
        ("caller", "", &caller_head),
        (crate::tasks::HOST, "", &main_head),
        ("caller", "HEAD", &main_head),
    ] {
        let mut headers = HeaderMap::new();
        headers.insert(
            crate::shared::protocol::SESSION_HEADER,
            caller.parse().unwrap(),
        );
        let Proto(preview) = preview_worktree(
            State(manager.clone()),
            Extension(Cap::Root),
            headers,
            Proto(wire::CreateWorktreeReq {
                project: "repo".into(),
                base: Some(base.into()),
                ..Default::default()
            }),
        )
        .await
        .unwrap();
        assert_eq!(&preview.commit, expected);
        // Worker creation uses this same resolution before allocating its checkout.
        assert_eq!(
            preview.commit,
            worktrees.worktree_base(caller, "repo", base).await.unwrap()
        );
    }
    std::fs::remove_dir_all(root).unwrap();
}

#[tokio::test]
async fn root_worktree_listing_does_not_require_an_agent_caller() {
    use crate::config::{Config, HostTerminalCfg, ProjectCfg};
    let manager = crate::application::test_application(Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: "/tmp".into(),
            ..Default::default()
        }],
        host_terminals: vec![HostTerminalCfg {
            name: "shell".into(),
            autostart: false,
            ..Default::default()
        }],
        ..Default::default()
    });
    manager.sessions.sync_from_config().await;
    // Worker ownership must still reject the same host terminal.
    assert!(
        Workers::new(&manager)
            .worker_project("shell", "repo")
            .await
            .unwrap_err()
            .to_string()
            .contains("A host terminal cannot own a worker")
    );
    for caller in [None, Some("host"), Some("shell"), Some("missing-session")] {
        let mut headers = HeaderMap::new();
        if let Some(caller) = caller {
            headers.insert(
                crate::shared::protocol::SESSION_HEADER,
                caller.parse().unwrap(),
            );
        }
        let Proto(reply) = list_worktrees(
            State(manager.clone()),
            Extension(Cap::Root),
            headers,
            Query(WorktreeQuery {
                project: "repo".into(),
            }),
        )
        .await
        .unwrap();
        assert_eq!(reply.worktrees.len(), 1);
        assert_eq!(reply.worktrees[0].id, "main");
    }
}

#[tokio::test]
async fn pending_relocation_remains_listable_with_storage_only_recovery_evidence() {
    use crate::config::{Config, ProjectCfg};
    use crate::worktrees::{RelocationIntent, Store, Worktree, WorktreePhase};
    let manager = crate::application::test_application(Config {
        projects: vec![ProjectCfg {
            id: "1111111111111111".into(),
            name: "repo".into(),
            dir: "/tmp".into(),
            ..Default::default()
        }],
        ..Default::default()
    });
    let source = manager
        .sessions
        .cfg_path()
        .parent()
        .unwrap()
        .join("missing-source");
    let destination = manager
        .sessions
        .cfg_path()
        .parent()
        .unwrap()
        .join("missing-destination");
    let evidence = format!("Inspect {} and {}", source.display(), destination.display());
    manager
        .sessions
        .save_worktrees(&Store {
            worktrees: vec![Worktree {
                id: "2222222222222222".into(),
                project_id: "1111111111111111".into(),
                name: "tree".into(),
                path: source.to_str().unwrap().into(),
                repository: "/tmp/repo/.git".into(),
                phase: WorktreePhase::Relocating,
                error: evidence.clone(),
                relocation: Some(RelocationIntent {
                    source: source.to_str().unwrap().into(),
                    destination: destination.to_str().unwrap().into(),
                }),
                ..Default::default()
            }],
        })
        .await
        .unwrap();
    let mut headers = HeaderMap::new();
    headers.insert(
        crate::shared::protocol::SESSION_HEADER,
        "host".parse().unwrap(),
    );
    let Proto(reply) = list_worktrees(
        State(manager.clone()),
        Extension(Cap::Root),
        headers,
        Query(WorktreeQuery {
            project: "repo".into(),
        }),
    )
    .await
    .unwrap();
    let pending = reply
        .worktrees
        .iter()
        .find(|w| w.id == "2222222222222222")
        .unwrap();
    assert_eq!(pending.phase, "relocating");
    assert_eq!(pending.error, evidence);
    assert_eq!(pending.path, source.to_str().unwrap());
    assert!(
        manager.sessions.worktree_records().worktrees[0]
            .relocation
            .is_some()
    );
}

#[tokio::test]
async fn scoped_worktree_requests_cannot_cross_projects_or_register_host_paths() {
    use crate::config::{Config, ProjectCfg, SessionCfg};
    let manager = crate::application::test_application(Config {
        projects: vec![
            ProjectCfg {
                name: "own".into(),
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
            project: "own".into(),
            ..Default::default()
        }],
        ..Default::default()
    });
    let cap = || {
        Cap::Scoped(crate::grant::Grant {
            grantor: "caller".into(),
            sessions: Default::default(),
            level: crate::grant::Level::Rw,
            revoked: Default::default(),
        })
    };
    let result = list_worktrees(
        State(manager.clone()),
        Extension(cap()),
        {
            let mut headers = HeaderMap::new();
            headers.insert(
                crate::shared::protocol::SESSION_HEADER,
                "host".parse().unwrap(),
            );
            headers
        },
        Query(WorktreeQuery {
            project: "other".into(),
        }),
    )
    .await;
    assert_eq!(result.unwrap_err().0, StatusCode::BAD_REQUEST);
    let result = Box::pin(create_worktree(
        State(manager.clone()),
        Extension(cap()),
        HeaderMap::new(),
        Proto(wire::CreateWorktreeReq {
            project: "own".into(),
            path: Some("/tmp/another-checkout".into()),
            ..Default::default()
        }),
    ))
    .await;
    assert_eq!(result.unwrap_err().0, StatusCode::FORBIDDEN);
    assert!(manager.sessions.worktree_records().worktrees.is_empty());
}
