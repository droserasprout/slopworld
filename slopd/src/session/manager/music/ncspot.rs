//! Native Linux proof of concept. ncspot owns Spotify credentials and playback.
//! SlopWorld owns one host terminal and a private IPC runtime directory.
//! It does not control other players owned by the user.
use crate::session::*;
use anyhow::{Context, Result};
use std::path::{Path, PathBuf};
use tokio::io::{AsyncBufReadExt, AsyncReadExt, AsyncWriteExt, BufReader};

#[derive(Default)]
pub(super) struct Player {
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
    // a default volume to a player with a saved applied volume. A missing marker means
    // IPC may never have become ready before shutdown, so volume still needs applying.
    pub(super) async fn recover_ncspot(&self, root: &Path) {
        let mut player = self.music.ncspot.lock().await;
        if player.session.is_some() {
            return;
        }
        if let Some(session) = self.adopted_ncspot().await {
            let applied_volume = self.tmux.ncspot_volume(&session).await;
            let volume = applied_volume.unwrap_or(1.0);
            *player = Player {
                session: Some(session),
                socket: Some(root.join("slopworld-ncspot/ncspot/ncspot.sock")),
                volume,
                applied_volume,
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

    pub(super) async fn open_ncspot(
        self: &Arc<Self>,
        cols: Option<u16>,
        rows: Option<u16>,
        volume: Option<f32>,
    ) -> Result<String> {
        let root = runtime()?;
        let mut player = self.music.ncspot.lock().await;
        let command = launch_command(&root);
        // The explicit tmux marker survives daemon deployment. Labels and reconstructed commands do not.
        if player.session.is_none() {
            player.session = self.adopted_ncspot().await;
        }
        if let Some(name) = player.session.clone() {
            if self.tmux.is_ncspot(&name).await {
                if let Some(volume) = volume {
                    player.volume = volume.clamp(0.0, 1.0);
                }
                player.socket = Some(root.join("slopworld-ncspot/ncspot/ncspot.sock"));
                self.music.audio.stop();
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
        anyhow::ensure!(
            tokio::net::UnixStream::connect(&socket).await.is_err(),
            "An unmanaged ncspot uses the SlopWorld socket. Close it before opening this player."
        );
        self.music.audio.stop();
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

    pub(super) async fn stop_ncspot(self: &Arc<Self>) -> Result<()> {
        let mut player = self.music.ncspot.lock().await;
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
}

impl Player {
    pub(super) fn set_volume(&mut self, volume: f32) -> bool {
        if self.session.is_none() {
            return false;
        }
        self.volume = volume.clamp(0.0, 1.0);
        true
    }

    pub(super) async fn state(&mut self, manager: &Manager) -> Option<crate::audio::AudioState> {
        let name = self.session.as_ref()?;
        let mut state = crate::audio::AudioState {
            source: Some("ncspot".into()),
            session: Some(name.clone()),
            volume: self.volume,
            ..Default::default()
        };
        if !manager.tmux.is_ncspot(name).await {
            state.error = Some("ncspot exited. Open Spotify again to restart it.".into());
            return Some(state);
        }
        let Some(socket) = self.socket.as_ref() else {
            return Some(state);
        };
        let volume = (self.applied_volume != Some(self.volume)).then_some(self.volume);
        match snapshot(socket, volume).await {
            Ok((playing, title)) => {
                state.playing = playing;
                state.title = title;
                if volume.is_some() {
                    if let Err(e) = manager.tmux.set_ncspot_volume(name, self.volume).await {
                        tracing::warn!("could not remember ncspot volume: {e:#}");
                    }
                }
                self.applied_volume = Some(self.volume);
            }
            // ncspot authenticates before creating its socket.
            // Keep the terminal usable during login. A missing socket does not indicate playback failure.
            Err(e) => tracing::debug!("ncspot IPC not ready: {e:#}"),
        }
        Some(state)
    }
}

impl crate::tmux::Tmux {
    async fn mark_ncspot(&self, name: &str) -> Result<()> {
        self.set_option(name, "@slopworld_ncspot", "1").await?;
        Ok(())
    }

    async fn is_ncspot(&self, name: &str) -> bool {
        self.option(name, "@slopworld_ncspot").await.as_deref() == Some("1")
    }

    async fn ncspot_volume(&self, name: &str) -> Option<f32> {
        self.option(name, "@slopworld_ncspot_volume")
            .await?
            .parse::<f32>()
            .ok()
            .filter(|volume| (0.0..=1.0).contains(volume))
    }

    async fn set_ncspot_volume(&self, name: &str, volume: f32) -> Result<()> {
        self.set_option(name, "@slopworld_ncspot_volume", &volume.to_string())
            .await?;
        Ok(())
    }
}

// ncspot uses floor(u16::MAX / 100) per percent: 100 steps leave 35 units.
// A separate one-percent command reaches the endpoint without overflowing its u16
// multiplication (a single 101-percent command would overflow).
fn volume_commands(volume: f32) -> String {
    let percent = (volume.clamp(0.0, 1.0) * 100.0).round() as u32;
    let mut commands = format!("voldown 100\nvoldown 1\nvolup {percent}\n");
    if percent == 100 {
        commands.push_str("volup 1\n");
    }
    commands
}

async fn snapshot(socket: &Path, volume: Option<f32>) -> Result<(bool, Option<String>)> {
    tokio::time::timeout(Duration::from_millis(250), async {
        let mut stream = tokio::net::UnixStream::connect(socket).await?;
        if let Some(volume) = volume {
            stream.write_all(volume_commands(volume).as_bytes()).await?;
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
#[path = "ncspot_tests.rs"]
mod tests;
