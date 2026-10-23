//! Published daemon events and their shared encoded representation.

use super::projects::ProjectView;
use crate::config::LibraryItemCfg;
use crate::session::{ScreenView, SessionView};
use std::sync::{Arc, OnceLock};

/// Producers share one channel and one encoded message per publication.
#[derive(Clone)]
pub(crate) struct EventPublisher(tokio::sync::broadcast::Sender<Arc<EventMessage>>);

impl EventPublisher {
    pub(super) fn new(capacity: usize) -> Self {
        Self(tokio::sync::broadcast::channel(capacity).0)
    }

    pub(super) fn subscribe(&self) -> tokio::sync::broadcast::Receiver<Arc<EventMessage>> {
        self.0.subscribe()
    }

    pub(crate) fn emit(&self, event: Event) {
        drop(self.0.send(EventMessage::new(event)));
    }
}

#[derive(Debug, Clone)]
pub enum Event {
    Capabilities {
        capabilities: crate::runtime::Capabilities,
    },
    Sessions {
        sessions: Vec<SessionView>,
    },
    Projects {
        projects: Vec<ProjectView>,
    },
    Library {
        library: Vec<LibraryItemCfg>,
    },
    Screen {
        screen: ScreenView,
    },
    Clipboard {
        name: String,
        run_id: u64,
        text: String,
    },
    Usage {
        usage: crate::usage::Snapshot,
    },
    Audio {
        audio: crate::audio::AudioState,
    },
    Jukebox {
        jukebox: crate::jukebox::Catalog,
    },
}

/// Shared event with lazy wire encoding. Keep the payload available for per-client filtering.
pub(crate) struct EventMessage {
    event: Event,
    encoded: OnceLock<Result<Arc<[u8]>, String>>,
}

impl EventMessage {
    pub(crate) fn new(event: Event) -> Arc<Self> {
        Arc::new(Self {
            event,
            encoded: OnceLock::new(),
        })
    }

    pub(crate) fn event(&self) -> &Event {
        &self.event
    }

    /// The transport supplies the encoder; all subscribers share its accepted bytes or error.
    pub(crate) fn encode_with(
        &self,
        encode: impl FnOnce(&Event) -> Result<Arc<[u8]>, String>,
    ) -> Result<Arc<[u8]>, String> {
        self.encoded.get_or_init(|| encode(&self.event)).clone()
    }
}

#[cfg(test)]
#[path = "events_tests.rs"]
mod tests;
