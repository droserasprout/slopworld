//! Creation and restoration must leave every previous namespace owner untouched.
use super::*;
use crate::config::HostTerminalCfg;

#[derive(Clone, Copy, Debug)]
enum ReservedName {
    StoppedShell,
    RunningShell,
    TemporaryAgent,
    HostAction,
    TmuxOnly,
}

struct CollisionFixture {
    _socket: crate::test_support::TmuxSocket,
    manager: Arc<SessionRuntime>,
    session: SessionCfg,
    archived: Option<std::path::PathBuf>,
    saved_host: bool,
    running: bool,
    pid: Option<u32>,
    role: Option<SessionRole>,
    accepted: String,
    on_disk: String,
}

impl CollisionFixture {
    async fn new(owner: ReservedName, restore: bool) -> Self {
        let socket = crate::test_support::TmuxSocket::new();
        let saved_host = matches!(
            owner,
            ReservedName::StoppedShell | ReservedName::RunningShell
        );
        let manager = crate::session::test_manager_with_socket(
            Config {
                projects: vec![ProjectCfg {
                    name: "repo".into(),
                    dir: "/tmp".into(),
                    ..Default::default()
                }],
                host_terminals: if saved_host {
                    vec![HostTerminalCfg {
                        name: "reserved".into(),
                        project: "repo".into(),
                        path: "/tmp".into(),
                        autostart: false,
                        ..Default::default()
                    }]
                } else {
                    vec![]
                },
                ..Default::default()
            },
            socket.path.clone(),
        );
        let session = SessionCfg {
            name: "reserved".into(),
            project: "repo".into(),
            state_id: crate::storage_id::draft_identity(),
            cmd: Some("true".into()),
            ..Default::default()
        };
        let archived = if restore {
            let private = crate::sandbox::state_dir(&session).unwrap();
            std::fs::create_dir_all(&private).unwrap();
            std::fs::write(private.join("memory"), "retained private state").unwrap();
            Some(
                crate::sandbox::trash_state(&session, "deleted")
                    .unwrap()
                    .unwrap(),
            )
        } else {
            None
        };
        let running = matches!(owner, ReservedName::RunningShell | ReservedName::TmuxOnly);
        if running {
            manager
                .tmux
                .spawn("reserved", "/tmp", 80, 24, &["cat".into()], false)
                .await
                .unwrap();
        }
        let pid = manager.tmux.pane_pid("reserved").await;
        if running {
            assert!(pid.is_some());
        }
        let role = match owner {
            ReservedName::RunningShell => Some(SessionRole::HostShell),
            ReservedName::TemporaryAgent => Some(SessionRole::Agent(AgentLifetime::Temporary)),
            ReservedName::HostAction => Some(SessionRole::HostAction),
            ReservedName::StoppedShell | ReservedName::TmuxOnly => None,
        };
        if let Some(role) = role {
            let mut live = Live::new(session.clone(), TitleCapture::default());
            live.role = role;
            manager.live.write().await.insert("reserved".into(), live);
        }
        let accepted = toml::to_string(&manager.config().await).unwrap();
        let on_disk = toml::to_string(&Config::load(manager.cfg_path()).await.unwrap()).unwrap();
        Self {
            _socket: socket,
            manager,
            session,
            archived,
            saved_host,
            running,
            pid,
            role,
            accepted,
            on_disk,
        }
    }

    async fn assert_preserved(&self) {
        let Self {
            manager,
            session,
            archived,
            running,
            pid,
            role,
            accepted,
            on_disk,
            ..
        } = self;
        assert_eq!(toml::to_string(&manager.config().await).unwrap(), *accepted);
        assert_eq!(
            toml::to_string(&Config::load(manager.cfg_path()).await.unwrap()).unwrap(),
            *on_disk
        );
        assert_eq!(manager.session_role("reserved").await, *role);
        assert_eq!(manager.tmux.pane_pid("reserved").await, *pid);
        if *running {
            assert!(manager.tmux.pane_exit("reserved").await.unwrap().is_none());
        }
        if let Some(path) = archived {
            assert_eq!(
                std::fs::read_to_string(path.join("memory")).unwrap(),
                "retained private state"
            );
            assert_eq!(
                crate::sandbox::trashed_session(path.file_name().unwrap().to_str().unwrap())
                    .unwrap()
                    .state_id,
                session.state_id
            );
            assert!(!crate::sandbox::state_dir(session).unwrap().exists());
        }
    }

    async fn release_and_retry(self) {
        let Self {
            manager,
            session,
            archived,
            saved_host,
            running,
            ..
        } = &self;
        let edits = manager.edits();
        // Removing the prior identity releases its name for a legitimate retry.
        if *running {
            manager.tmux.kill("reserved").await.unwrap();
        }
        manager.live.write().await.remove("reserved");
        if *saved_host {
            crate::application::Configuration::new(&crate::application::Application::from_runtime(
                manager,
            ))
            .update_cfg(ConfigMutation::HostShells, |cfg| {
                cfg.host_terminals.clear();
                Ok(())
            })
            .await
            .unwrap();
        }
        if let Some(path) = archived {
            edits
                .restore_stored_state(path.file_name().unwrap().to_str().unwrap())
                .await
                .unwrap();
            assert!(!path.exists());
            assert_eq!(
                std::fs::read_to_string(crate::sandbox::state_dir(session).unwrap().join("memory"))
                    .unwrap(),
                "retained private state"
            );
        } else {
            edits.add(session.clone()).await.unwrap();
        }
        assert!(
            Config::load(manager.cfg_path())
                .await
                .unwrap()
                .session("reserved")
                .is_some()
        );
    }
}

async fn check_collision(owner: ReservedName, restore: bool) {
    let fixture = CollisionFixture::new(owner, restore).await;
    let edits = fixture.manager.edits();
    let error = if let Some(path) = &fixture.archived {
        edits
            .restore_stored_state(path.file_name().unwrap().to_str().unwrap())
            .await
            .unwrap_err()
    } else {
        edits.add(fixture.session.clone()).await.unwrap_err()
    };
    assert!(
        error.to_string().contains("already exists"),
        "{owner:?}: {error:#}"
    );
    fixture.assert_preserved().await;
    fixture.release_and_retry().await;
}

#[tokio::test]
async fn creation_rejects_saved_live_and_tmux_name_owners_then_allows_retry() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    for owner in [
        ReservedName::StoppedShell,
        ReservedName::RunningShell,
        ReservedName::TemporaryAgent,
        ReservedName::HostAction,
        ReservedName::TmuxOnly,
    ] {
        check_collision(owner, false).await;
    }
}

#[tokio::test]
async fn restoration_rejects_name_owners_before_moving_archive_then_allows_retry() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    for owner in [
        ReservedName::StoppedShell,
        ReservedName::RunningShell,
        ReservedName::TemporaryAgent,
        ReservedName::HostAction,
        ReservedName::TmuxOnly,
    ] {
        check_collision(owner, true).await;
    }
}
