//! Playback-state polling and change publication; music owns players and transitions.
use super::Services;
use std::sync::Arc;

impl Services {
    /// Schedule combined player-state publication without coupling audio output to events.
    pub(crate) fn spawn_music_poll(self: &Arc<Self>) -> tokio::task::JoinHandle<()> {
        let services = self.clone();
        tokio::spawn(async move {
            let mut last = None;
            let mut tick = tokio::time::interval(std::time::Duration::from_millis(500));
            tick.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Delay);
            loop {
                tick.tick().await;
                services.publish_music_if_changed(&mut last).await;
            }
        })
    }

    async fn publish_music_if_changed(&self, last: &mut Option<crate::audio::AudioState>) {
        let now = self.music.state().await;
        if last.as_ref() == Some(&now) {
            return;
        }
        *last = Some(now.clone());
        self.emit(crate::application::Event::Audio { audio: now });
    }
}

#[cfg(test)]
#[path = "music_tests.rs"]
mod tests;
