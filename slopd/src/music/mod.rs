//! Playback selection, recovery, and transitions shared by every transport.
//! audio owns decoding/output, jukebox owns catalog storage, and ncspot owns IPC.
//! HostSessions is the sole session integration; no manager state is accessed here.
use anyhow::Result;
use std::future::Future;

mod ncspot;
mod request;
pub(crate) use request::AudioReq;
#[cfg(test)]
pub(crate) use request::AudioSelection;
use request::{AudioSource, Change};

/// Session lifecycle operations needed by the managed Spotify player.
/// Implementations retain their normal lifecycle guards and rollback behavior,
/// using the same tmux server supplied when constructing Music.
pub(crate) trait HostSessions: Sync {
    fn host_sessions(&self) -> impl Future<Output = Vec<String>> + Send;
    fn launch(
        &self,
        command: String,
        cols: Option<u16>,
        rows: Option<u16>,
    ) -> impl Future<Output = Result<String>> + Send;
    fn stop(&self, name: &str) -> impl Future<Output = Result<()>> + Send;
}

pub(crate) struct Music {
    audio: crate::audio::Audio,
    tmux: crate::tmux::Tmux,
    // Held through the complete source change and construction of its direct reply.
    transition: tokio::sync::Mutex<()>,
    // Polling retains this guard through IPC and volume-marker persistence so stop/open
    // cannot reuse its socket. Lock order: transition -> ncspot; polling takes only ncspot.
    ncspot: tokio::sync::Mutex<ncspot::Player>,
}

impl Music {
    pub(crate) fn new(tmux: crate::tmux::Tmux) -> Self {
        Self {
            audio: crate::audio::Audio::new(),
            tmux,
            transition: tokio::sync::Mutex::new(()),
            ncspot: tokio::sync::Mutex::new(Default::default()),
        }
    }

    pub(crate) async fn state(&self) -> crate::audio::AudioState {
        self.ncspot
            .lock()
            .await
            .state(&self.tmux)
            .await
            .unwrap_or_else(|| self.audio.state())
    }

    async fn volume(&self, volume: f32) {
        if !self.ncspot.lock().await.set_volume(volume) {
            self.audio.set_volume(volume);
        }
    }

    /// Startup calls recovery after session adoption and before serving requests.
    pub(crate) async fn recover(&self, sessions: &impl HostSessions) {
        if let Ok(root) = ncspot::runtime() {
            self.recover_ncspot(sessions, &root).await;
        }
    }

    pub(crate) async fn open_spotify(
        &self,
        sessions: &impl HostSessions,
        cols: Option<u16>,
        rows: Option<u16>,
    ) -> Result<String> {
        let _transition = self.transition.lock().await;
        self.open_ncspot(sessions, cols, rows, None).await
    }

    pub(crate) async fn select(
        &self,
        sessions: &impl HostSessions,
        req: AudioReq,
    ) -> Option<crate::audio::AudioState> {
        let spotify = req.wants_spotify();
        let volume = req.volume;
        let _transition = self.transition.lock().await;
        let result = match req.change() {
            Ok(change) => self.apply(sessions, change, volume).await,
            Err(error) => Err(error),
        };
        if let Err(error) = result {
            let error = format!("{error:#}");
            self.audio.reject(error.clone());
            if spotify {
                return Some(crate::audio::AudioState {
                    source: Some("ncspot".into()),
                    error: Some(error),
                    ..Default::default()
                });
            }
        }
        // Reopening an existing player still needs a reply. Preserve ordered socket
        // launch/stop handling even when no broadcast state changed.
        if spotify {
            Some(self.state().await)
        } else {
            None
        }
    }

    async fn apply(&self, sessions: &impl HostSessions, change: Change, volume: f32) -> Result<()> {
        match change {
            Change::Volume => self.volume(volume).await,
            Change::Spotify => {
                self.open_ncspot(sessions, None, None, Some(volume)).await?;
            }
            Change::Stop => {
                self.stop_ncspot(sessions).await?;
                self.audio.stop();
            }
            Change::Audio(source) => {
                // Catalog resolution must succeed before stopping the current source.
                let source = resolve_audio_source(source)?;
                self.stop_ncspot(sessions).await?;
                self.audio.play(&source, volume);
            }
        }
        Ok(())
    }
}

fn resolve_audio_source(source: AudioSource) -> Result<String> {
    match source {
        AudioSource::File(file) => Ok(file),
        AudioSource::Station { station, stream } => crate::jukebox::catalog()
            .resolve(&station, &stream)
            .map_err(|e| anyhow::anyhow!("jukebox selection rejected: {e:#}")),
    }
}

#[cfg(test)]
mod tests;
