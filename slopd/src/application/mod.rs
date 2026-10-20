//! Daemon-wide publication, task storage and playback services, constructed at startup.
//! Session runtime supplies identity and operation leases; API owns wire encoding.
//! Configuration and lifecycle coordination remain in session::Manager.
mod events;
mod music;
mod usage;
use crate::tasks::io::TaskStore;
use anyhow::{Context, Result};
pub(crate) use events::{Event, EventMessage};
use std::sync::Arc;
use tokio::sync::{RwLock, broadcast};

pub(crate) struct Services {
    tasks: TaskStore,
    pub(crate) music: crate::music::Music,
    usage: RwLock<crate::usage::Snapshot>,
    events: broadcast::Sender<Arc<EventMessage>>,
}

impl Services {
    pub(crate) async fn load(data: &std::path::Path, tmux: crate::tmux::Tmux) -> Result<Arc<Self>> {
        let tasks = TaskStore::load(data.to_owned())
            .await
            .with_context(|| format!("loading task store in {}", data.display()))?;
        Ok(Arc::new(Self::new(tasks, tmux, 256)))
    }

    pub(crate) fn new(tasks: TaskStore, tmux: crate::tmux::Tmux, event_capacity: usize) -> Self {
        let (events, _) = broadcast::channel(event_capacity);
        Self {
            tasks,
            events,
            music: crate::music::Music::new(tmux),
            usage: RwLock::new(Default::default()),
        }
    }

    pub(crate) fn tasks(&self, lease: impl Send + 'static) -> crate::tasks::io::TaskIo {
        self.tasks.access(lease)
    }

    pub(crate) fn subscribe(&self) -> broadcast::Receiver<Arc<EventMessage>> {
        self.events.subscribe()
    }

    pub(crate) fn emit(&self, event: Event) {
        drop(self.events.send(EventMessage::new(event)));
    }
}

#[cfg(test)]
mod test_support;
