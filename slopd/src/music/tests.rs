use super::*;
use crate::shared::wire::{AudioRequest, AudioSelection, audio_request::Change as WireChange};
use std::sync::{
    Mutex,
    atomic::{AtomicBool, AtomicUsize, Ordering},
};

fn request(change: Option<WireChange>, volume: Option<f32>) -> AudioReq {
    AudioRequest {
        change,
        volume,
        ..Default::default()
    }
    .into()
}

fn transport(action: &str, volume: Option<f32>) -> AudioReq {
    request(Some(WireChange::Transport(action.into())), volume)
}

fn spotify_request(id: &str, volume: Option<f32>) -> AudioReq {
    AudioRequest {
        change: Some(WireChange::Selection(AudioSelection {
            ncspot: true,
            ..Default::default()
        })),
        request_id: Some(id.into()),
        volume,
        ..Default::default()
    }
    .into()
}

fn track(id: &str, volume: Option<f32>) -> AudioReq {
    request(
        Some(WireChange::Selection(AudioSelection {
            track: Some(id.into()),
            ..Default::default()
        })),
        volume,
    )
}

fn stop(token: Option<&str>) -> AudioReq {
    AudioRequest {
        change: Some(WireChange::Stop(Default::default())),
        stop_token: token.map(str::to_owned),
        ..Default::default()
    }
    .into()
}

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

#[tokio::test]
async fn transports_cannot_bypass_the_music_transition_gate() {
    let owner = crate::test_support::TmuxSocket::new();
    let tmux = crate::tmux::Tmux::new(&owner.path);
    let sessions = TestSessions::new(Vec::new(), tmux.clone());
    let music = Music::new(tmux);
    let transition = music.transition.lock().await;
    let socket = music.select(&sessions, request(None, Some(0.42)));
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
    let reply = music
        .select(&sessions, stop(Some("failed-stop")))
        .await
        .unwrap();
    assert!(reply.stop_ack.is_none());
    assert_eq!(music.state().await.session.as_deref(), Some("player"));
    assert!(tmux.exists("player").await);
    sessions.fail_stop.store(false, Ordering::SeqCst);
    for token in ["retry", "repeated"] {
        let reply = music.select(&sessions, stop(Some(token))).await.unwrap();
        assert_eq!(reply.stop_ack.as_deref(), Some(token));
        assert!(!reply.playing);
        assert!(reply.source.is_none());
        assert!(reply.error.is_none());
        assert!(
            music.state().await.stop_ack.is_none(),
            "acknowledgments are not persistent state"
        );
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
    for selection in [
        AudioSelection {
            ncspot: true,
            station: Some("radio".into()),
            ..Default::default()
        },
        AudioSelection {
            stream: Some("orphan".into()),
            ..Default::default()
        },
        AudioSelection {
            station: Some("nonexistent-refactor-fixture".into()),
            stream: Some("missing".into()),
            ..Default::default()
        },
    ] {
        let spotify = selection.ncspot;
        let req = request(Some(WireChange::Selection(selection)), Some(0.5));
        let reply = music.select(&sessions, req).await;
        if spotify {
            assert!(reply.unwrap().error.unwrap().contains("not both"));
        } else {
            assert!(reply.unwrap().error.is_some());
        }
        assert_eq!(sessions.stops.load(Ordering::SeqCst), 0);
        assert_eq!(music.state().await.session.as_deref(), Some("player"));
        assert!(tmux.exists("player").await);
    }
}

pub(crate) fn configure_test_output(command: &mut std::process::Command, root: &std::path::Path) {
    // A test-only ALSA configuration exposes a usable default without hardware.
    let config = root.join("asound.conf");
    std::fs::write(&config, "pcm.!default { type null }").unwrap();
    command.env("ALSA_CONFIG_PATH", config);
}

fn player_runtime(with_output: bool) -> Option<std::path::PathBuf> {
    use std::os::unix::fs::PermissionsExt;
    crate::test_support::isolated_with_env(|command, root| {
        if with_output {
            configure_test_output(command, root);
        } else {
            let config = root.join("asound.conf");
            std::fs::write(&config, "").unwrap();
            command.env("ALSA_CONFIG_PATH", config);
        }
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
    let Some(_) = player_runtime(true) else {
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
    let opening = || spotify_request("opening-one", Some(0.42));
    let reply = music.select(&sessions, opening()).await.unwrap();
    assert_eq!(reply.session.as_deref(), Some(first.as_str()));
    assert_eq!(reply.request_id.as_deref(), Some("opening-one"));
    assert!(music.state().await.request_id.is_none());
    assert_eq!(reply.volume.to_bits(), 0.42_f32.to_bits());
    assert_eq!(sessions.launches.load(Ordering::SeqCst), 1);
    assert!(
        music
            .select(&sessions, request(None, Some(2.0)))
            .await
            .is_none()
    );
    assert_eq!(music.state().await.volume.to_bits(), 1.0_f32.to_bits());
    tmux.kill(&first).await.unwrap();
    assert!(music.state().await.error.unwrap().contains("exited"));
    let second = music.select(&sessions, opening()).await.unwrap();
    assert!(second.error.is_none());
    assert_ne!(second.session.as_deref(), Some(first.as_str()));
    assert_eq!(sessions.launches.load(Ordering::SeqCst), 2);
    music.select(&sessions, stop(None)).await;
    assert!(music.state().await.session.is_none());
}

#[tokio::test]
async fn unmanaged_socket_is_rejected_before_session_launch() {
    let Some(root) = player_runtime(true) else {
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
    let poll = music.poll();
    tokio::pin!(poll);
    // Drive polling to its IPC boundary, then hold the reply while stop competes.
    let mut stream = tokio::select! {
        accepted = listener.accept() => accepted.unwrap().0,
        _ = &mut poll => panic!("poll completed before the server accepted IPC"),
    };
    let stop = music.select(&sessions, stop(None));
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
        resolve_station("fixture", "high").unwrap(),
        "https://example.invalid/stream"
    );
    resolve_station("fixture", "missing").unwrap_err();
}

#[tokio::test]
async fn paused_track_survives_connections_and_queued_navigation_uses_latest_selection() {
    let Some(_) = crate::test_support::isolated_with_env(|command, _| {
        command.env_remove("SLOPD_RUNTIME");
    }) else {
        return;
    };
    let owner = crate::test_support::TmuxSocket::new();
    let tmux = crate::tmux::Tmux::new(&owner.path);
    let sessions = TestSessions::new(Vec::new(), tmux.clone());
    let mut music = Music::new(tmux);
    music.audio = crate::audio::Audio::simulated();
    music.select(&sessions, request(None, Some(0.37))).await;
    music.select(&sessions, transport("ost", None)).await;
    let admitted = music.transition.lock().await;
    let next = music.select(&sessions, transport("next", Some(0.99)));
    let pause = music.select(&sessions, transport("pause", None));
    tokio::pin!(next, pause);
    // Both clients have sent commands while still observing the first track.
    assert!(futures::poll!(next.as_mut()).is_pending());
    assert!(futures::poll!(pause.as_mut()).is_pending());
    drop(admitted);
    assert!(next.await.is_none());
    assert!(pause.await.is_none());
    let paused = music.state().await;
    let second = crate::ost::tracks().nth(1).unwrap();
    assert_eq!(
        paused.selection.as_ref().unwrap().track.as_deref(),
        Some(second)
    );
    assert!(!paused.active && !paused.playing && paused.source.is_none() && paused.error.is_none());
    assert_eq!(paused.volume.to_bits(), 0.37_f32.to_bits());
    assert_eq!(music.poll().await, paused);
    let invalid = music
        .select(&sessions, track("../../game.ogg", None))
        .await
        .unwrap();
    assert!(invalid.error.unwrap().contains("Unknown OST track"));
    assert_eq!(music.state().await, paused);
    music.select(&sessions, transport("play", None)).await;
    let resumed = tokio::time::timeout(std::time::Duration::from_secs(5), async {
        loop {
            let state = music.state().await;
            assert!(state.error.is_none(), "{state:?}");
            if state.playing {
                return state;
            }
            tokio::task::yield_now().await;
        }
    })
    .await
    .unwrap();
    assert!(resumed.active);
    assert_eq!(resumed.selection, paused.selection);
    assert_eq!(resumed.volume.to_bits(), paused.volume.to_bits());
    assert_eq!(resumed.source, Some(format!("ost:{second}")));
}

#[test]
fn station_navigation_wraps_and_preserves_the_closest_quality() {
    use crate::jukebox::{Catalog, Station, Stream};
    let station = |id: &str, rates: &[u32]| Station {
        id: id.into(),
        metadata: Default::default(),
        default_rate: rates[0],
        streams: rates
            .iter()
            .map(|rate| Stream {
                rate: *rate,
                key: rate.to_string(),
                url: String::new(),
            })
            .collect(),
    };
    let catalog = Catalog {
        stations: vec![
            station("one", &[128]),
            station("hidden", &[128]),
            station("two", &[160, 96]),
        ],
    };
    let one = Source::Station {
        station: "one".into(),
        stream: "128".into(),
    };
    let two = Source::Station {
        station: "two".into(),
        stream: "96".into(),
    };
    let hidden = ["hidden".into()].into();
    for direction in [Direction::Next, Direction::Previous, Direction::Shuffle] {
        assert_eq!(
            direction.navigate(Some(&one), &catalog, &hidden).unwrap(),
            two
        );
        assert_eq!(
            direction.navigate(Some(&two), &catalog, &hidden).unwrap(),
            one
        );
    }
    Direction::Next
        .navigate(Some(&Source::Spotify), &catalog, &hidden)
        .unwrap_err();
}

#[tokio::test]
async fn spotify_without_output_leaves_the_resumable_selection_and_gain_intact() {
    let Some(_) = player_runtime(false) else {
        return;
    };
    let owner = crate::test_support::TmuxSocket::new();
    let tmux = crate::tmux::Tmux::new(&owner.path);
    let sessions = TestSessions::new(Vec::new(), tmux.clone());
    let mut music = Music::new(tmux);
    music.audio = crate::audio::Audio::simulated();
    music
        .select(&sessions, track("pace-20261012", Some(0.41)))
        .await;
    music.select(&sessions, transport("pause", None)).await;
    let paused = music.state().await;
    let rejected = music
        .select(&sessions, spotify_request("opening-unavailable", None))
        .await
        .unwrap();
    assert_eq!(rejected.request_id.as_deref(), Some("opening-unavailable"));
    assert!(rejected.error.unwrap().contains("No host audio output"));
    assert_eq!(sessions.launches.load(Ordering::SeqCst), 0);
    assert_eq!(music.state().await, paused);
}
