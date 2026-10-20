use super::*;
use std::sync::{
    Mutex,
    atomic::{AtomicBool, AtomicUsize, Ordering},
};

/// Exercise service decisions against real tmux markers without running Spotify.
pub(super) struct TestSessions {
    names: Mutex<Vec<String>>,
    tmux: crate::tmux::Tmux,
    stops: AtomicUsize,
    launches: AtomicUsize,
    fail_stop: AtomicBool,
}

impl TestSessions {
    pub(super) fn new(names: Vec<String>, tmux: crate::tmux::Tmux) -> Self {
        Self {
            names: Mutex::new(names),
            tmux,
            stops: AtomicUsize::new(0),
            launches: AtomicUsize::new(0),
            fail_stop: AtomicBool::new(false),
        }
    }
}

impl HostSessions for TestSessions {
    async fn host_sessions(&self) -> Vec<String> {
        self.names.lock().unwrap().clone()
    }
    async fn launch(
        &self,
        command: String,
        cols: Option<u16>,
        rows: Option<u16>,
    ) -> Result<String> {
        assert!(command.ends_with(" ncspot"));
        let n = self.launches.fetch_add(1, Ordering::SeqCst);
        let name = format!("player-{n}");
        self.tmux
            .spawn(
                &name,
                "/tmp",
                cols.unwrap_or(80),
                rows.unwrap_or(24),
                &["sleep".into(), "60".into()],
                true,
            )
            .await?;
        self.names.lock().unwrap().push(name.clone());
        Ok(name)
    }
    async fn stop(&self, name: &str) -> Result<()> {
        self.stops.fetch_add(1, Ordering::SeqCst);
        anyhow::ensure!(
            !self.fail_stop.load(Ordering::SeqCst),
            "injected stop failure"
        );
        self.tmux.kill(name).await?;
        self.names.lock().unwrap().retain(|n| n != name);
        Ok(())
    }
}

#[test]
fn resolve_audio_accepts_a_plain_file() {
    assert_eq!(
        resolve_audio_source(AudioSource::File("/tmp/song.mp3".into())).unwrap(),
        "/tmp/song.mp3"
    );
}

#[tokio::test]
async fn transports_cannot_bypass_the_music_transition_gate() {
    let owner = crate::test_support::TmuxSocket::new();
    let tmux = crate::tmux::Tmux::new(&owner.path);
    let sessions = TestSessions::new(Vec::new(), tmux.clone());
    let music = Music::new(tmux);
    let transition = music.transition.lock().await;
    let socket = music.select(
        &sessions,
        AudioReq {
            selection: None,
            volume: 0.42,
        },
    );
    let http = music.open_spotify(&sessions, None, None);
    tokio::pin!(socket, http);
    assert!(futures::poll!(socket.as_mut()).is_pending());
    assert!(futures::poll!(http.as_mut()).is_pending());
    drop(transition);
    assert!(socket.await.is_none());
    assert_eq!(sessions.launches.load(Ordering::SeqCst), 0);
}

#[tokio::test]
async fn failed_stop_retains_player_for_retry_and_repeated_stop_is_harmless() {
    let owner = crate::test_support::TmuxSocket::new();
    let tmux = crate::tmux::Tmux::new(&owner.path);
    tmux.spawn(
        "player",
        "/tmp",
        80,
        24,
        &["sleep".into(), "60".into()],
        true,
    )
    .await
    .unwrap();
    tmux.mark_ncspot("player").await.unwrap();
    let sessions = TestSessions::new(vec!["player".into()], tmux.clone());
    let music = Music::new(tmux.clone());
    let root = crate::test_support::fixture("music-stop");
    music.recover_ncspot(&sessions, &root).await;
    sessions.fail_stop.store(true, Ordering::SeqCst);
    let error = music.apply(&sessions, Change::Stop, 1.0).await.unwrap_err();
    assert!(error.to_string().contains("injected stop failure"));
    assert_eq!(music.state().await.session.as_deref(), Some("player"));
    assert!(tmux.exists("player").await);
    sessions.fail_stop.store(false, Ordering::SeqCst);
    for _ in 0..2 {
        music.apply(&sessions, Change::Stop, 1.0).await.unwrap();
    }
    assert_eq!(sessions.stops.load(Ordering::SeqCst), 2);
    assert!(!tmux.exists("player").await);
    assert!(music.state().await.session.is_none());
}

#[tokio::test]
async fn invalid_selection_and_missing_station_leave_managed_player_running() {
    let owner = crate::test_support::TmuxSocket::new();
    let tmux = crate::tmux::Tmux::new(&owner.path);
    tmux.spawn(
        "player",
        "/tmp",
        80,
        24,
        &["sleep".into(), "60".into()],
        true,
    )
    .await
    .unwrap();
    tmux.mark_ncspot("player").await.unwrap();
    let sessions = TestSessions::new(vec!["player".into()], tmux.clone());
    let music = Music::new(tmux.clone());
    let root = crate::test_support::fixture("music-invalid");
    music.recover_ncspot(&sessions, &root).await;
    for json in [
        r#"{"volume":0.5,"selection":{"ncspot":true,"file":"song"}}"#,
        r#"{"volume":0.5,"selection":{"stream":"orphan"}}"#,
        r#"{"volume":0.5,"selection":{"station":"nonexistent-refactor-fixture","stream":"missing"}}"#,
    ] {
        let req: AudioReq = serde_json::from_str(json).unwrap();
        let spotify = req.wants_spotify();
        let reply = music.select(&sessions, req).await;
        if spotify {
            assert!(reply.unwrap().error.unwrap().contains("not both"));
        } else {
            assert!(reply.is_none());
        }
        assert_eq!(sessions.stops.load(Ordering::SeqCst), 0);
        assert_eq!(music.state().await.session.as_deref(), Some("player"));
        assert!(tmux.exists("player").await);
    }
}

fn player_runtime() -> Option<std::path::PathBuf> {
    use std::os::unix::fs::PermissionsExt;
    crate::test_support::isolated_with_env(|command, root| {
        let bin = root.join("bin");
        std::fs::create_dir(&bin).unwrap();
        let executable = bin.join("ncspot");
        std::fs::write(&executable, "#!/bin/sh\nexit 0\n").unwrap();
        std::fs::set_permissions(&executable, std::fs::Permissions::from_mode(0o700)).unwrap();
        let mut paths = vec![bin];
        paths.extend(std::env::split_paths(
            &std::env::var_os("PATH").unwrap_or_default(),
        ));
        command
            .env("PATH", std::env::join_paths(paths).unwrap())
            .env("XDG_RUNTIME_DIR", root)
            .env_remove("SLOPD_RUNTIME");
    })
}

#[tokio::test]
async fn spotify_open_reuses_player_replies_updates_volume_and_recovers_after_exit() {
    let Some(_) = player_runtime() else {
        return;
    };
    let owner = crate::test_support::TmuxSocket::new();
    let tmux = crate::tmux::Tmux::new(&owner.path);
    let sessions = TestSessions::new(Vec::new(), tmux.clone());
    let music = Music::new(tmux.clone());
    let first = music
        .open_spotify(&sessions, Some(90), Some(30))
        .await
        .unwrap();
    assert!(tmux.is_ncspot(&first).await);
    assert_eq!(
        music.open_spotify(&sessions, None, None).await.unwrap(),
        first
    );
    let request = || {
        serde_json::from_str::<AudioReq>(r#"{"volume":0.42,"selection":{"ncspot":true}}"#).unwrap()
    };
    let reply = music.select(&sessions, request()).await.unwrap();
    assert_eq!(reply.session.as_deref(), Some(first.as_str()));
    assert_eq!(reply.volume.to_bits(), 0.42_f32.to_bits());
    assert_eq!(sessions.launches.load(Ordering::SeqCst), 1);
    assert!(
        music
            .select(
                &sessions,
                AudioReq {
                    volume: 2.0,
                    selection: None
                }
            )
            .await
            .is_none()
    );
    assert_eq!(music.state().await.volume.to_bits(), 1.0_f32.to_bits());
    tmux.kill(&first).await.unwrap();
    assert!(music.state().await.error.unwrap().contains("exited"));
    let second = music.select(&sessions, request()).await.unwrap();
    assert!(second.error.is_none());
    assert_ne!(second.session.as_deref(), Some(first.as_str()));
    assert_eq!(sessions.launches.load(Ordering::SeqCst), 2);
    music
        .select(
            &sessions,
            AudioReq {
                volume: 1.0,
                selection: Some(None),
            },
        )
        .await;
    assert!(music.state().await.session.is_none());
}

#[tokio::test]
async fn unmanaged_socket_is_rejected_before_session_launch() {
    let Some(root) = player_runtime() else {
        return;
    };
    let socket_dir = root.join("slopworld-ncspot/ncspot");
    std::fs::create_dir_all(&socket_dir).unwrap();
    let _listener = tokio::net::UnixListener::bind(socket_dir.join("ncspot.sock")).unwrap();
    let owner = crate::test_support::TmuxSocket::new();
    let tmux = crate::tmux::Tmux::new(&owner.path);
    let sessions = TestSessions::new(Vec::new(), tmux.clone());
    let music = Music::new(tmux);
    let error = music.open_spotify(&sessions, None, None).await.unwrap_err();
    assert!(error.to_string().contains("unmanaged ncspot"));
    assert_eq!(sessions.launches.load(Ordering::SeqCst), 0);
}

#[tokio::test]
async fn polling_holds_player_ownership_until_ipc_and_volume_persistence_finish() {
    use tokio::io::AsyncWriteExt;
    let root = crate::test_support::fixture("music-poll");
    let socket_dir = root.join("slopworld-ncspot/ncspot");
    std::fs::create_dir_all(&socket_dir).unwrap();
    let listener = tokio::net::UnixListener::bind(socket_dir.join("ncspot.sock")).unwrap();
    let owner = crate::test_support::TmuxSocket::new();
    let tmux = crate::tmux::Tmux::new(&owner.path);
    tmux.spawn(
        "player",
        "/tmp",
        80,
        24,
        &["sleep".into(), "60".into()],
        true,
    )
    .await
    .unwrap();
    tmux.mark_ncspot("player").await.unwrap();
    let sessions = TestSessions::new(vec!["player".into()], tmux.clone());
    let music = Music::new(tmux.clone());
    music.recover_ncspot(&sessions, &root).await;
    let poll = music.state();
    tokio::pin!(poll);
    // Drive polling to its IPC boundary, then hold the reply while stop competes.
    let mut stream = tokio::select! {
        accepted = listener.accept() => accepted.unwrap().0,
        _ = &mut poll => panic!("poll completed before the server accepted IPC"),
    };
    let stop = music.select(
        &sessions,
        AudioReq {
            volume: 1.0,
            selection: Some(None),
        },
    );
    tokio::pin!(stop);
    assert!(futures::poll!(stop.as_mut()).is_pending());
    assert_eq!(sessions.stops.load(Ordering::SeqCst), 0);
    stream
        .write_all(b"{\"mode\":\"Stopped\",\"playable\":null}\n")
        .await
        .unwrap();
    let state = poll.await;
    assert_eq!(state.session.as_deref(), Some("player"));
    assert_eq!(tmux.ncspot_volume("player").await, Some(1.0));
    assert!(stop.await.is_none());
    assert_eq!(sessions.stops.load(Ordering::SeqCst), 1);
    assert!(music.state().await.session.is_none());
}

#[test]
fn station_selection_resolves_catalog_keys_before_playback() {
    let Some(_) = crate::test_support::isolated_with_env(|command, root| {
        command.env("SLOPD_CONFIG_ROOT", root.join("config"));
    }) else {
        return;
    };
    crate::jukebox::save_user(crate::jukebox::Station {
        id: "fixture".into(),
        default_rate: 96,
        metadata: crate::jukebox::Metadata {
            name: "Fixture".into(),
            donate: String::new(),
            title_regex: String::new(),
        },
        streams: vec![crate::jukebox::Stream {
            rate: 96,
            key: "high".into(),
            url: "https://example.invalid/stream".into(),
        }],
    })
    .unwrap();
    crate::jukebox::reload().unwrap();
    assert_eq!(
        resolve_audio_source(AudioSource::Station {
            station: "fixture".into(),
            stream: "high".into()
        })
        .unwrap(),
        "https://example.invalid/stream"
    );
    resolve_audio_source(AudioSource::Station {
        station: "fixture".into(),
        stream: "missing".into(),
    })
    .unwrap_err();
}
