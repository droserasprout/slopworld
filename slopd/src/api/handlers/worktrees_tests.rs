use super::*;

#[tokio::test]
async fn pending_relocation_remains_listable_with_storage_only_recovery_evidence() {
    use crate::config::{Config, ProjectCfg};
    use crate::worktrees::{RelocationIntent, Store, Worktree, WorktreePhase};
    let manager = crate::session::test_manager(Config {
        projects: vec![ProjectCfg {
            id: "1111111111111111".into(),
            name: "repo".into(),
            dir: "/tmp".into(),
            ..Default::default()
        }],
        ..Default::default()
    });
    let source = manager.cfg_path.parent().unwrap().join("missing-source");
    let destination = manager
        .cfg_path
        .parent()
        .unwrap()
        .join("missing-destination");
    let evidence = format!("Inspect {} and {}", source.display(), destination.display());
    manager
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
    assert!(manager.worktree_records().worktrees[0].relocation.is_some());
}

#[tokio::test]
async fn scoped_worktree_requests_cannot_cross_projects_or_register_host_paths() {
    use crate::config::{Config, ProjectCfg, SessionCfg};
    let manager = crate::session::test_manager(Config {
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
        HeaderMap::new(),
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
    assert!(manager.worktree_records().worktrees.is_empty());
}
