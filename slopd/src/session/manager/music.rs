//! Adapt music's host-terminal operations to the session lifecycle.
//! The music service owns playback state, transition locks, recovery, and ncspot IPC.
use super::Manager;
use crate::config::LibraryItemKind;
use crate::session::{
    ExecutionSource, HostLifetime, LaunchContent, LaunchDestination, LaunchOptions, LaunchRequest,
};
use anyhow::Result;
use std::sync::Arc;

impl crate::music::HostSessions for Arc<Manager> {
    async fn host_sessions(&self) -> Vec<String> {
        self.live
            .read()
            .await
            .iter()
            .filter(|(_, live)| live.role.is_host())
            .map(|(name, _)| name.clone())
            .collect()
    }

    async fn launch(
        &self,
        command: String,
        cols: Option<u16>,
        rows: Option<u16>,
    ) -> Result<String> {
        self.run_launch(LaunchRequest {
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
        Manager::stop(self, name).await
    }
}

impl Manager {
    pub(crate) async fn open_spotify(
        self: &Arc<Self>,
        cols: Option<u16>,
        rows: Option<u16>,
    ) -> Result<String> {
        self.application.music.open_spotify(self, cols, rows).await
    }

    pub(crate) async fn select_music(
        self: &Arc<Self>,
        req: crate::music::AudioReq,
    ) -> Option<crate::audio::AudioState> {
        self.application.music.select(self, req).await
    }
}

#[cfg(test)]
#[path = "music_tests.rs"]
mod tests;
