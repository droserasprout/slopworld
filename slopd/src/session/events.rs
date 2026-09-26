//! Published daemon events and their shared encoded representation.

use super::{ProjectView, ScreenView, SessionView};
use crate::config::LibraryItemCfg;
use prost::Message;
use std::sync::{Arc, OnceLock};

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

/// One immutable event shared by all WebSocket pumps. The event itself remains separate from
/// its cached wire representation because scoped session lists may need a filtered envelope.
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

    pub(crate) fn encoded(&self) -> Result<Arc<[u8]>, String> {
        self.encoded
            .get_or_init(|| {
                let _perf = crate::perf::timer("websocket-serialize");
                self.event
                    .to_protobuf()
                    .map(|e| Arc::from(e.encode_to_vec()))
                    .map_err(|e| e.to_string())
            })
            .clone()
    }
}

#[cfg(test)]
#[path = "events_tests.rs"]
mod tests;
