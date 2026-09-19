//! Native Linux proof of concept. ncspot owns Spotify credentials and playback; SlopWorld
//! owns one host terminal and a private IPC runtime directory, never the user's other player.
use super::super::*;
use anyhow::{Context, Result};
use std::path::{Path, PathBuf};
use tokio::io::{AsyncBufReadExt, AsyncReadExt, AsyncWriteExt, BufReader};

#[derive(Default)]
pub(crate) struct Player {
    session: Option<String>,
    socket: Option<PathBuf>,
    volume: f32,
    applied_volume: Option<f32>,
}

pub(super) fn runtime() -> Result<PathBuf> {
    anyhow::ensure!(
        cfg!(target_os = "linux") && !crate::runtime::is_slopcar(),
        "ncspot playback requires a native Linux daemon"
    );
    dirs::runtime_dir().context("ncspot requires XDG_RUNTIME_DIR")
}

fn launch_command(root: &Path) -> String {
    let runtime = root.join("slopworld-ncspot");
    let pulse = std::env::var("PULSE_SERVER")
        .unwrap_or_else(|_| format!("unix:{}", root.join("pulse/native").display()));
    let quote = |s: &str| format!("'{}'", s.replace('\'', "'\\''"));
    format!(
        "env {} {} ncspot",
        quote(&format!("XDG_RUNTIME_DIR={}", runtime.display())),
        quote(&format!("PULSE_SERVER={pulse}"))
    )
}

impl Manager {
    // Called after startup adoption and before serving requests. Recovery must not send
    // a default volume to a player that kept running while the daemon was down.
    pub(super) async fn recover_ncspot(&self, root: &Path) {
        let mut player = self.ncspot.lock().await;
        if player.session.is_some() {
            return;
        }
        if let Some(session) = self.adopted_ncspot().await {
            let volume = self.tmux.ncspot_volume(&session).await.unwrap_or(1.0);
            *player = Player {
                session: Some(session),
                socket: Some(root.join("slopworld-ncspot/ncspot/ncspot.sock")),
                volume,
                applied_volume: Some(volume),
            };
        }
    }

    async fn adopted_ncspot(&self) -> Option<String> {
        let names: Vec<_> = self
            .live
            .read()
            .await
            .iter()
            .filter(|(_, live)| live.host)
            .map(|(name, _)| name.clone())
            .collect();
        for name in names {
            if self.tmux.is_ncspot(&name).await {
                return Some(name);
            }
        }
        None
    }

    pub(crate) async fn open_ncspot(
        self: &Arc<Self>,
        cols: Option<u16>,
        rows: Option<u16>,
        volume: Option<f32>,
    ) -> Result<String> {
        let root = runtime()?;
        let mut player = self.ncspot.lock().await;
        let command = launch_command(&root);
        // The explicit tmux marker survives redeploy; labels and reconstructed commands do not.
        if player.session.is_none() {
            player.session = self.adopted_ncspot().await;
        }
        if let Some(name) = player.session.clone() {
            if self.tmux.is_ncspot(&name).await {
                if let Some(volume) = volume {
                    player.volume = volume.clamp(0.0, 1.0);
                }
                player.socket = Some(root.join("slopworld-ncspot/ncspot/ncspot.sock"));
                self.audio.stop();
                return Ok(name);
            }
            player.session = None;
        }
        let output = crate::process::run_bounded(
            tokio::process::Command::new("ncspot").arg("--version"),
            Duration::from_secs(3),
            crate::process::CaptureLimits {
                stdout: 4096,
                stderr: 4096,
            },
        )
        .await
        .context("install ncspot on the daemon host first")?;
        anyhow::ensure!(output.status.success(), "ncspot --version failed");
        let directory = root.join("slopworld-ncspot");
        tokio::fs::create_dir_all(&directory).await?;
        use std::os::unix::fs::PermissionsExt;
        tokio::fs::set_permissions(&directory, std::fs::Permissions::from_mode(0o700)).await?;
        let socket = directory.join("ncspot/ncspot.sock");
        anyhow::ensure!(tokio::net::UnixStream::connect(&socket).await.is_err(),
            "an unmanaged ncspot is using the SlopWorld socket; close it before opening this player");
        self.audio.stop();
        let session = self
            .run_errand(
                LibraryItemCfg {
                    name: "Spotify".into(),
                    kind: LibraryItemKind::Shell,
                    command: Some(command),
                    host: true,
                    ..Default::default()
                },
                RunWhere {
                    temp: true,
                    cols,
                    rows,
                    ..Default::default()
                },
                true,
                false,
                "",
            )
            .await?;
        if let Err(error) = self.tmux.mark_ncspot(&session).await {
            self.stop(&session).await?;
            return Err(error);
        }
        player.session = Some(session.clone());
        player.socket = Some(socket);
        player.volume = volume.unwrap_or(1.0).clamp(0.0, 1.0);
        player.applied_volume = None;
        Ok(session)
    }

    pub(crate) async fn stop_ncspot(self: &Arc<Self>) -> Result<()> {
        let mut player = self.ncspot.lock().await;
        if player.session.is_none() {
            player.session = self.adopted_ncspot().await;
        }
        if let Some(name) = player.session.as_ref() {
            if self.tmux.is_ncspot(name).await {
                self.stop(name).await?;
            }
        }
        *player = Player::default();
        Ok(())
    }

    pub(crate) async fn music_volume(&self, volume: f32) -> Result<()> {
        let mut player = self.ncspot.lock().await;
        if player.session.is_some() {
            player.volume = volume.clamp(0.0, 1.0);
        } else {
            self.audio.set_volume(volume);
        }
        Ok(())
    }

    pub(crate) async fn music_state(&self) -> crate::audio::AudioState {
        let mut player = self.ncspot.lock().await;
        let Some(name) = player.session.as_ref() else {
            return self.audio.state();
        };
        let mut state = crate::audio::AudioState {
            source: Some("ncspot".into()),
            session: Some(name.clone()),
            volume: player.volume,
            ..Default::default()
        };
        if !self.tmux.is_ncspot(name).await {
            state.error = Some("ncspot exited; open Spotify again to restart it".into());
            return state;
        }
        let Some(socket) = player.socket.as_ref() else {
            return state;
        };
        let volume = (player.applied_volume != Some(player.volume)).then_some(player.volume);
        match snapshot(socket, volume).await {
            Ok((playing, title)) => {
                state.playing = playing;
                state.title = title;
                if volume.is_some() {
                    if let Err(e) = self.tmux.set_ncspot_volume(name, player.volume).await {
                        tracing::warn!("could not remember ncspot volume: {e:#}");
                    }
                }
                player.applied_volume = Some(player.volume);
            }
            // Authentication happens before ncspot creates its socket. Keep the terminal
            // usable while login is pending; a missing socket is not a playback failure.
            Err(e) => tracing::debug!("ncspot IPC not ready: {e:#}"),
        }
        state
    }
}

async fn snapshot(socket: &Path, volume: Option<f32>) -> Result<(bool, Option<String>)> {
    tokio::time::timeout(Duration::from_millis(250), async {
        let mut stream = tokio::net::UnixStream::connect(socket).await?;
        if let Some(volume) = volume {
            // ncspot exposes relative volume commands only. Reset then raise to the game's
            // absolute percentage, once per slider change (never once per status poll).
            stream
                .write_all(
                    format!("voldown 100\nvolup {}\n", (volume * 100.0).round() as u32).as_bytes(),
                )
                .await?;
        }
        let mut line = String::new();
        BufReader::new(stream)
            .take(64 * 1024)
            .read_line(&mut line)
            .await?;
        anyhow::ensure!(line.ends_with('\n'), "truncated ncspot status");
        parse_status(&line)
    })
    .await
    .context("ncspot IPC timed out")?
}

fn parse_status(line: &str) -> Result<(bool, Option<String>)> {
    let status: serde_json::Value = serde_json::from_str(line)?;
    let playing = status["mode"].get("Playing").is_some();
    let title = status["playable"]["title"].as_str().map(|title| {
        let artists = status["playable"]["artists"]
            .as_array()
            .map(|artists| {
                artists
                    .iter()
                    .filter_map(|v| v.as_str())
                    .collect::<Vec<_>>()
                    .join(", ")
            })
            .unwrap_or_default();
        if artists.is_empty() {
            title.into()
        } else {
            format!("{artists} - {title}")
        }
    });
    Ok((playing, title))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn recovery_reports_surviving_player_without_changing_its_volume() {
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
        old_tmux.set_ncspot_volume("player", 0.42).await.unwrap();

        let socket_dir = root.join("slopworld-ncspot/ncspot");
        tokio::fs::create_dir_all(&socket_dir).await.unwrap();
        let listener = tokio::net::UnixListener::bind(socket_dir.join("ncspot.sock")).unwrap();
        let server = tokio::spawn(async move {
            let (mut stream, _) = listener.accept().await.unwrap();
            stream
                .write_all(
                    b"{\"mode\":{\"Playing\":{}},\"playable\":{\"title\":\"Surviving song\"}}\n",
                )
                .await
                .unwrap();
            let mut commands = Vec::new();
            stream.read_to_end(&mut commands).await.unwrap();
            assert!(
                commands.is_empty(),
                "recovery must not change the player's volume"
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
        assert_eq!(state.volume, 0.42);
        server.await.unwrap();
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
            assert_eq!(command, "voldown 100\nvolup 42\n");
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
}
