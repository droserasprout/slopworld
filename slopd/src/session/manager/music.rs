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
            .filter(|(_, live)| live.host)
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
    /// Schedule combined player-state publication without coupling audio output to events.
    pub(crate) fn spawn_music_poll(self: &Arc<Self>) -> tokio::task::JoinHandle<()> {
        let manager = self.clone();
        tokio::spawn(async move {
            let mut last = None;
            let mut tick = tokio::time::interval(std::time::Duration::from_millis(500));
            tick.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Delay);
            loop {
                tick.tick().await;
                manager.publish_music_if_changed(&mut last).await;
            }
        })
    }

    async fn publish_music_if_changed(&self, last: &mut Option<crate::audio::AudioState>) {
        let now = self.music.state().await;
        if last.as_ref() == Some(&now) {
            return;
        }
        *last = Some(now.clone());
        self.emit(crate::session::Event::Audio { audio: now });
    }

    pub(crate) async fn music_state(&self) -> crate::audio::AudioState {
        self.music.state().await
    }

    pub(crate) async fn open_spotify(
        self: &Arc<Self>,
        cols: Option<u16>,
        rows: Option<u16>,
    ) -> Result<String> {
        self.music.open_spotify(self, cols, rows).await
    }

    pub(crate) async fn select_music(
        self: &Arc<Self>,
        req: crate::music::AudioReq,
    ) -> Option<crate::audio::AudioState> {
        self.music.select(self, req).await
    }
}

#[cfg(test)]
#[path = "music_tests.rs"]
mod tests;
