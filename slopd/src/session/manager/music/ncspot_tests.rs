use super::*;

#[test]
fn relative_commands_reach_volume_endpoints_from_any_start() {
    for initial in [0, 35, 32768, u16::MAX] {
        for target in [0.0, 0.42, 1.0] {
            let mut actual = initial;
            for line in volume_commands(target).lines() {
                let (command, amount) = line.split_once(' ').unwrap();
                let delta = (u16::MAX / 100)
                    .checked_mul(amount.parse().unwrap())
                    .unwrap();
                actual = match command {
                    "voldown" => actual.saturating_sub(delta),
                    "volup" => actual.saturating_add(delta),
                    _ => panic!("unexpected volume command"),
                };
            }
            let expected = if target == 1.0 {
                u16::MAX
            } else {
                (u16::MAX / 100) * (target * 100.0).round() as u16
            };
            assert_eq!(actual, expected, "initial={initial}, target={target}");
        }
    }
}

#[tokio::test]
async fn recovery_reports_surviving_player_without_changing_its_volume() {
    check_recovery(Some(0.42)).await;
}

#[tokio::test]
async fn recovery_applies_volume_when_no_applied_marker_exists() {
    check_recovery(None).await;
}

async fn check_recovery(saved_volume: Option<f32>) {
    let id = uuid::Uuid::new_v4();
    let tmux_socket = format!("slop-ncspot-recovery-{id}");
    let root = std::env::temp_dir().join(format!("ncspot-recovery-{id}"));
    struct Cleanup(String, PathBuf);
    impl Drop for Cleanup {
        fn drop(&mut self) {
            let _ = std::process::Command::new("tmux")
                .args(["-L", &self.0, "kill-server"])
                .output();
            let _ = std::fs::remove_dir_all(&self.1);
        }
    }
    let _cleanup = Cleanup(tmux_socket.clone(), root.clone());
    let output = tokio::process::Command::new("tmux")
        .args([
            "-L",
            &tmux_socket,
            "-f",
            "/dev/null",
            "new-session",
            "-d",
            "-s",
            "player",
            "--",
            "sleep",
            "60",
        ])
        .output()
        .await
        .unwrap();
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
    let old_tmux = crate::tmux::Tmux::new(&tmux_socket);
    if let Some(volume) = saved_volume {
        old_tmux.set_ncspot_volume("player", volume).await.unwrap();
    }

    let socket_dir = root.join("slopworld-ncspot/ncspot");
    tokio::fs::create_dir_all(&socket_dir).await.unwrap();
    let listener = tokio::net::UnixListener::bind(socket_dir.join("ncspot.sock")).unwrap();
    let server = tokio::spawn(async move {
        let (mut stream, _) = listener.accept().await.unwrap();
        stream
            .write_all(b"{\"mode\":{\"Playing\":{}},\"playable\":{\"title\":\"Surviving song\"}}\n")
            .await
            .unwrap();
        let mut commands = Vec::new();
        stream.read_to_end(&mut commands).await.unwrap();
        let expected = if saved_volume.is_some() {
            String::new()
        } else {
            volume_commands(1.0)
        };
        assert_eq!(
            commands,
            expected.as_bytes(),
            "recovery preserves applied volume but initializes an unapplied volume"
        );
    });

    let manager = crate::session::test_manager_with_socket(Config::default(), tmux_socket);
    let mut live = Live::new(SessionCfg::default(), TitleCapture::default());
    live.host = true;
    manager.live.write().await.insert("player".into(), live);
    manager.recover_ncspot(&root).await;
    assert!(
        manager.music_state().await.session.is_none(),
        "an ordinary host terminal is not a player"
    );
    old_tmux.mark_ncspot("player").await.unwrap();
    manager.recover_ncspot(&root).await;
    let state = manager.music_state().await;
    assert!(state.playing);
    assert_eq!(state.source.as_deref(), Some("ncspot"));
    assert_eq!(state.session.as_deref(), Some("player"));
    assert_eq!(state.title.as_deref(), Some("Surviving song"));
    assert_eq!(state.volume, saved_volume.unwrap_or(1.0));
    server.await.unwrap();
    manager.stop_ncspot().await.unwrap();
    manager.stop_ncspot().await.unwrap();
    assert!(!manager.tmux.exists("player").await);
    assert!(manager.music_state().await.session.is_none());
}

#[tokio::test]
async fn ipc_reads_initial_status_and_sets_absolute_volume() {
    let dir = std::env::temp_dir().join(format!("ncspot-test-{}", uuid::Uuid::new_v4()));
    tokio::fs::create_dir(&dir).await.unwrap();
    let path = dir.join("ipc.sock");
    let listener = tokio::net::UnixListener::bind(&path).unwrap();
    let server = tokio::spawn(async move {
        let (stream, _) = listener.accept().await.unwrap();
        let mut stream = BufReader::new(stream);
        let mut command = String::new();
        stream.read_line(&mut command).await.unwrap();
        stream.read_line(&mut command).await.unwrap();
        stream.read_line(&mut command).await.unwrap();
        assert_eq!(command, "voldown 100\nvoldown 1\nvolup 42\n");
        stream.get_mut().write_all(b"{\"mode\":{\"Playing\":{}},\"playable\":{\"title\":\"Song\",\"artists\":[\"A\",\"B\"]}}\n").await.unwrap();
    });
    assert_eq!(
        snapshot(&path, Some(0.42)).await.unwrap(),
        (true, Some("A, B - Song".into()))
    );
    server.await.unwrap();
    tokio::fs::remove_dir_all(dir).await.unwrap();
}
#[tokio::test]
async fn ipc_rejects_oversized_and_silent_responses() {
    for silent in [false, true] {
        let dir = std::env::temp_dir().join(format!("ncspot-test-{}", uuid::Uuid::new_v4()));
        tokio::fs::create_dir(&dir).await.unwrap();
        let path = dir.join("ipc.sock");
        let listener = tokio::net::UnixListener::bind(&path).unwrap();
        let server = tokio::spawn(async move {
            let (mut stream, _) = listener.accept().await.unwrap();
            if !silent {
                let _ = stream.write_all(&vec![b'x'; 64 * 1024]).await;
            }
            std::future::pending::<()>().await;
        });
        let result = snapshot(&path, None).await.unwrap_err();
        if silent {
            assert!(result.to_string().contains("timed out"));
        } else {
            assert!(result.to_string().contains("truncated"));
        }
        server.abort();
        let _ = server.await;
        tokio::fs::remove_dir_all(dir).await.unwrap();
    }
}

#[test]
fn status_handles_paused_stopped_and_missing_track() {
    assert_eq!(
        parse_status(r#"{"mode":"Stopped","playable":null}"#).unwrap(),
        (false, None)
    );
    assert_eq!(
        parse_status(r#"{"mode":{"Paused":0},"playable":{"title":"Episode"}}"#).unwrap(),
        (false, Some("Episode".into()))
    );
    assert!(parse_status("not json").is_err());
}
#[tokio::test]
async fn missing_socket_is_a_bounded_error() {
    assert!(
        snapshot(Path::new("/nonexistent/slopworld-ncspot.sock"), None)
            .await
            .is_err()
    );
}

#[tokio::test]
async fn ncspot_marker_survives_new_handle_and_does_not_mark_other_tabs() {
    let socket = format!("slop-ncspot-{}", uuid::Uuid::new_v4());
    let tmux = crate::tmux::Tmux::new(&socket);
    struct Cleanup(String);
    impl Drop for Cleanup {
        fn drop(&mut self) {
            let _ = std::process::Command::new("tmux")
                .args(["-L", &self.0, "kill-server"])
                .output();
        }
    }
    let _cleanup = Cleanup(socket.clone());
    for name in ["player", "other"] {
        let output = tokio::process::Command::new("tmux")
            .args([
                "-L",
                &socket,
                "-f",
                "/dev/null",
                "new-session",
                "-d",
                "-s",
                name,
                "--",
                "sleep",
                "60",
            ])
            .output()
            .await
            .unwrap();
        assert!(output.status.success());
    }
    tmux.mark_ncspot("player").await.unwrap();
    let adopted = crate::tmux::Tmux::new(&socket);
    assert!(adopted.is_ncspot("player").await);
    assert!(!adopted.is_ncspot("other").await);
    adopted.kill("player").await.unwrap();
    assert!(!adopted.exists("player").await);
    assert!(adopted.exists("other").await);
}
