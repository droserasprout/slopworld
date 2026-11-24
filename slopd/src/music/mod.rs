//! Playback selection, recovery, and transitions shared by every transport.
//! audio owns decoding/output, jukebox owns catalog storage, and ncspot owns IPC.
//! HostSessions is the sole session integration; no manager state is accessed here.
use anyhow::Result;
use std::future::Future;

mod navigation;
mod ncspot;
mod request;
pub(crate) use request::AudioReq;
use request::{Change, Direction, Reply, Source};

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

#[derive(Debug)]
struct Shared {
    selection: Option<Source>,
    volume: f32,
}

impl Default for Shared {
    fn default() -> Self {
        Self {
            selection: None,
            volume: 1.0,
        }
    }
}

pub(crate) struct Music {
    audio: crate::audio::Audio,
    tmux: crate::tmux::Tmux,
    // FIFO admission orders source changes, relative transport, and complete snapshots.
    transition: tokio::sync::Mutex<Shared>,
    ncspot: tokio::sync::Mutex<ncspot::Player>,
}

impl Music {
    pub(crate) fn new(tmux: crate::tmux::Tmux) -> Self {
        Self {
            audio: crate::audio::Audio::new(),
            tmux,
            transition: Default::default(),
            ncspot: Default::default(),
        }
    }

    pub(crate) async fn configure_output(&self, settings: crate::config::AudioOutputSettings) {
        let _transition = self.transition.lock().await;
        self.audio.configure_output(settings);
    }

    pub(crate) fn output_device(&self) -> Option<String> {
        self.audio.state().output_device
    }

    pub(crate) async fn state(&self) -> crate::audio::AudioState {
        let shared = self.transition.lock().await;
        self.snapshot(&shared, false).await
    }

    async fn snapshot(&self, shared: &Shared, sync_volume: bool) -> crate::audio::AudioState {
        let mut state = self
            .ncspot
            .lock()
            .await
            .state(&self.tmux, sync_volume)
            .await
            .unwrap_or_else(|| self.audio.state());
        state.selection = shared.selection.as_ref().map(Into::into);
        state.volume = shared.volume;
        state
    }

    // Only the publication timer advances drained tracks. Snapshot reads and
    // client connections never issue playback commands.
    pub(crate) async fn poll(&self) -> crate::audio::AudioState {
        let mut shared = self.transition.lock().await;
        if let Some(completed) = self.audio.completed_track()
            && matches!(&shared.selection, Some(Source::Track(track)) if track == &completed)
            && let Ok(next) = Direction::Next.track(Some(&completed))
        {
            self.audio
                .play(&format!("{}{next}", crate::ost::PREFIX), shared.volume);
            shared.selection = Some(Source::Track(next));
            if let Err(error) = self.audio.flush().await {
                self.audio.reject(format!("{error:#}"));
            }
        }
        self.snapshot(&shared, true).await
    }

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
        let mut shared = self.transition.lock().await;
        let session = self
            .open_ncspot(sessions, cols, rows, Some(shared.volume))
            .await?;
        shared.selection = Some(Source::Spotify);
        Ok(session)
    }

    pub(crate) async fn select(
        &self,
        sessions: &impl HostSessions,
        req: AudioReq,
    ) -> Option<crate::audio::AudioState> {
        let mut shared = self.transition.lock().await;
        let result = match req.change {
            Ok(change) => self.apply(sessions, &mut shared, change, req.volume).await,
            Err(error) => Err(error),
        };
        if let Err(error) = result {
            let mut reply = self.snapshot(&shared, false).await;
            reply.error = Some(format!("{error:#}"));
            if let Reply::Spotify(request_id) = req.reply {
                reply.request_id = request_id;
                reply.source = Some("ncspot".into());
                reply.session = None;
            }
            return Some(reply);
        }
        let (stop_ack, request_id) = match req.reply {
            Reply::None => return None,
            Reply::Stop(token) => (Some(token), None),
            Reply::Spotify(id) => (None, id),
        };
        let mut reply = self.snapshot(&shared, false).await;
        reply.stop_ack = stop_ack;
        reply.request_id = request_id;
        Some(reply)
    }

    async fn apply(
        &self,
        sessions: &impl HostSessions,
        shared: &mut Shared,
        change: Change,
        volume: Option<f32>,
    ) -> Result<()> {
        let gain = volume.unwrap_or(shared.volume);
        let selection = match change {
            Change::Volume => {
                if volume.is_some() && !self.ncspot.lock().await.set_volume(gain) {
                    self.audio.set_volume(gain);
                }
                shared.volume = gain;
                return Ok(());
            }
            Change::Stop => {
                self.stop_ncspot(sessions).await?;
                self.audio.stop_confirmed().await?;
                // Native stop messages historically carry the client's gain. Only
                // explicit volume/selection writes change gain; transport preserves it.
                return Ok(());
            }
            Change::Play => match &shared.selection {
                Some(current) => current.clone(),
                None => Source::Track(Direction::Next.track(None)?),
            },
            Change::Navigate(direction, hidden) => direction.navigate(
                shared.selection.as_ref(),
                &crate::jukebox::catalog(),
                &hidden,
            )?,
            Change::Select(source) => source,
        };
        let source = match &selection {
            Source::Spotify => {
                self.open_ncspot(sessions, None, None, Some(gain)).await?;
                None
            }
            Source::Station { station, stream } => Some(resolve_station(station, stream)?),
            Source::Track(track) => {
                crate::ost::bytes(track)?;
                Some(format!("{}{track}", crate::ost::PREFIX))
            }
        };
        if let Some(source) = source.as_deref() {
            anyhow::ensure!(
                !crate::runtime::is_slopcar(),
                "Server audio output is unavailable in slopcar."
            );
            self.stop_ncspot(sessions).await?;
            self.audio.play(source, gain);
        }
        // Sending Play commits its generation. Retain the corresponding selection
        // even if the caller is cancelled or the worker misses the reply deadline.
        shared.selection = Some(selection);
        shared.volume = gain;
        if source.is_some() {
            self.audio.flush().await?;
        }
        Ok(())
    }
}

fn resolve_station(station: &str, stream: &str) -> Result<String> {
    crate::jukebox::catalog()
        .resolve(station, stream)
        .map_err(|e| anyhow::anyhow!("jukebox selection rejected: {e:#}"))
}

#[cfg(test)]
mod tests;
#[cfg(test)]
pub(crate) use tests::configure_test_output;
