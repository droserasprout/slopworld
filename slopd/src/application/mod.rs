//! Daemon-wide publication and durable task services.
//! Session runtime supplies identity and operation leases; API owns wire encoding.
//! Configuration and lifecycle coordination remain in session::Manager.
mod events;
use crate::tasks::io::TaskStore;
pub(crate) use events::{Event, EventMessage};
use std::sync::Arc;
use tokio::sync::broadcast;

pub(crate) struct Services {
    tasks: TaskStore,
    events: broadcast::Sender<Arc<EventMessage>>,
}

impl Services {
    pub(crate) fn new(tasks: TaskStore, event_capacity: usize) -> Self {
        let (events, _) = broadcast::channel(event_capacity);
        Self { tasks, events }
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
