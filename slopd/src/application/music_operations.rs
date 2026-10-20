//! Coordinate playback requests and adapt host lifecycle to music's player service.
use super::{Application, launch::Launches};
use crate::config::LibraryItemKind;
use crate::session::{
    ExecutionSource, HostLifetime, LaunchContent, LaunchDestination, LaunchOptions, LaunchRequest,
    SessionRuntime,
};
use anyhow::Result;
use std::sync::Arc;

pub(crate) struct MusicOperations<'a> {
    sessions: Arc<SessionRuntime>,
    launches: Launches,
    music: &'a crate::music::Music,
}

impl<'a> MusicOperations<'a> {
    pub(crate) fn new(application: &'a Application) -> Self {
        Self {
            sessions: application.sessions.clone(),
            launches: Launches::new(application),
            music: &application.services.music,
        }
    }
}

impl crate::music::HostSessions for MusicOperations<'_> {
    async fn host_sessions(&self) -> Vec<String> {
        self.sessions.host_sessions().await
    }

    async fn launch(
        &self,
        command: String,
        cols: Option<u16>,
        rows: Option<u16>,
    ) -> Result<String> {
        self.launches
            .run_launch(LaunchRequest {
                content: LaunchContent {
                    name: "Spotify".into(),
                    kind: LibraryItemKind::Shell,
                    command: Some(command),
                    text: String::new(),
                },
                execution: ExecutionSource::Host(HostLifetime::Disposable),
                destination: LaunchDestination::Temporary,
                options: LaunchOptions {
                    cols,
                    rows,
                    ..Default::default()
                },
            })
            .await
    }

    async fn stop(&self, name: &str) -> Result<()> {
        self.sessions.stop(name).await
    }
}

impl MusicOperations<'_> {
    pub(crate) async fn open_spotify(
        &self,
        cols: Option<u16>,
        rows: Option<u16>,
    ) -> Result<String> {
        self.music.open_spotify(self, cols, rows).await
    }

    pub(crate) async fn select_music(
        &self,
        req: crate::music::AudioReq,
    ) -> Option<crate::audio::AudioState> {
        self.music.select(self, req).await
    }
}

#[cfg(test)]
#[path = "music_operations_tests.rs"]
mod tests;
