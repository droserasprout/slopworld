use super::*;

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
    assert!(crate::worktrees::Store::load(&manager.cfg_path)
        .await
        .unwrap()
        .worktrees
        .is_empty());
}
