//! Playback selection, recovery and transition serialization for every transport.
//! ncspot owns the managed terminal and IPC; audio owns decoded OST/radio playback.
//! The transition gate covers each complete source change, including its direct reply.

use super::Manager;
use anyhow::{bail, Result};
use serde::Deserialize;
use std::sync::Arc;

mod ncspot;

/// A jukebox request. Omit `selection` to change only the volume. Use null to stop playback.
/// Select a catalog entry with a station ID and stream key, or select soundtrack files with a file or directory path.
#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct AudioReq {
    #[serde(default, deserialize_with = "some_option")]
    pub(crate) selection: Option<Option<AudioSelection>>,
    pub(crate) volume: f32,
}

#[derive(Deserialize)]
pub(crate) struct AudioSelection {
    #[serde(default)]
    pub(crate) station: Option<String>,
    #[serde(default)]
    pub(crate) stream: Option<String>,
    #[serde(default)]
    pub(crate) file: Option<String>,
    #[serde(default)]
    pub(crate) ncspot: bool,
}

/// Distinguish an absent selection from a null selection.
/// An absent selection preserves playback. A null selection stops playback.
fn some_option<'de, D, T>(d: D) -> Result<Option<Option<T>>, D::Error>
where
    D: serde::Deserializer<'de>,
    T: Deserialize<'de>,
{
    Option::deserialize(d).map(Some)
}

/// Radio audio and ncspot share a gate for playback source changes.
pub(crate) struct MusicState {
    audio: crate::audio::Audio,
    transition: tokio::sync::Mutex<()>,
    // Operation guard, not a shared session-state lock: polling must finish its IPC
    // and volume marker write before stop/reopen can reuse the same socket.
    // Lock order is transition -> ncspot; polling takes only ncspot.
    ncspot: tokio::sync::Mutex<ncspot::Player>,
}

impl MusicState {
    pub(super) fn new() -> Self {
        Self {
            audio: crate::audio::Audio::new(),
            transition: tokio::sync::Mutex::new(()),
            ncspot: tokio::sync::Mutex::new(Default::default()),
        }
    }
}

impl Manager {
    pub(crate) async fn music_state(&self) -> crate::audio::AudioState {
        self.music
            .ncspot
            .lock()
            .await
            .state(self)
            .await
            .unwrap_or_else(|| self.music.audio.state())
    }

    async fn music_volume(&self, volume: f32) -> Result<()> {
        if !self.music.ncspot.lock().await.set_volume(volume) {
            self.music.audio.set_volume(volume);
        }
        Ok(())
    }

    pub(super) async fn recover_music(&self) {
        if let Ok(root) = ncspot::runtime() {
            self.recover_ncspot(&root).await;
        }
    }

    /// HTTP and socket callers share the same source transition boundary.
    pub(crate) async fn open_spotify(
        self: &Arc<Self>,
        cols: Option<u16>,
        rows: Option<u16>,
    ) -> Result<String> {
        let _transition = self.music.transition.lock().await;
        self.open_ncspot(cols, rows, None).await
    }

    pub(crate) async fn select_music(
        self: &Arc<Self>,
        req: AudioReq,
    ) -> Option<crate::audio::AudioState> {
        let spotify = req
            .selection
            .as_ref()
            .and_then(Option::as_ref)
            .is_some_and(|s| s.ncspot);
        let _transition = self.music.transition.lock().await;
        let result = match req.selection {
            Some(Some(selection)) if selection.ncspot => {
                if selection.station.is_some()
                    || selection.stream.is_some()
                    || selection.file.is_some()
                {
                    Err(anyhow::anyhow!(
                        "Select ncspot or another audio source, not both."
                    ))
                } else {
                    self.open_ncspot(None, None, Some(req.volume))
                        .await
                        .map(|_| ())
                }
            }
            Some(selection) => {
                // Validation precedes playback changes under the same transition guard.
                let source = selection.map(resolve_audio_source).transpose();
                match source {
                    Err(error) => Err(error),
                    Ok(source) => match self.stop_ncspot().await {
                        Err(error) => Err(error),
                        Ok(()) => {
                            match source {
                                Some(source) => self.music.audio.play(&source, req.volume),
                                None => self.music.audio.stop(),
                            }
                            Ok(())
                        }
                    },
                }
            }
            None => self.music_volume(req.volume).await,
        };
        if let Err(e) = result {
            let error = format!("{e:#}");
            self.music.audio.reject(error.clone());
            if spotify {
                return Some(crate::audio::AudioState {
                    source: Some("ncspot".into()),
                    error: Some(error),
                    ..Default::default()
                });
            }
        }
        // Reply even when the player was already open and its broadcast state did not change.
        // Socket launch and shutdown use the same ordered connection.
        if spotify {
            Some(self.music_state().await)
        } else {
            None
        }
    }
}

fn resolve_audio_source(selection: AudioSelection) -> anyhow::Result<String> {
    anyhow::ensure!(
        !selection.ncspot,
        "The daemon does not decode audio from ncspot."
    );
    match (selection.station, selection.stream, selection.file) {
        (Some(station), Some(stream), None) => crate::jukebox::catalog()
            .resolve(&station, &stream)
            .map_err(|e| anyhow::anyhow!("jukebox selection rejected: {e:#}")),
        (None, None, Some(file)) => Ok(file),
        _ => bail!("Select a station and stream, or select a file."),
    }
}

#[cfg(test)]
#[path = "music_tests.rs"]
mod tests;
