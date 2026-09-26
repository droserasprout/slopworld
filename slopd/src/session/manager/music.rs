//! Music playback services and transition serialization.

use super::ncspot;

/// Radio audio and ncspot share a gate for playback source changes.
pub(crate) struct MusicState {
    pub(crate) audio: crate::audio::Audio,
    pub(crate) transition: tokio::sync::Mutex<()>,
    pub(super) ncspot: tokio::sync::Mutex<ncspot::Player>,
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
