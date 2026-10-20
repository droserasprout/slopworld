//! Daemon-wide publication, task storage and playback services, constructed at startup.
//! Session runtime supplies identity and operation leases; API owns wire encoding.
//! Session runtime retains live validation, operation guards, and reconciliation.
mod agent_templates;
pub(crate) use agent_templates::{AgentTemplates, TemplatePersistence};
mod config_authority;
pub(crate) use config_authority::{AuthChange, ConfigAuthority};
mod events;
mod library;
mod music;
pub(crate) mod projects;
pub(crate) use library::{LibraryCatalog, LibraryMutation, LibraryRuntime};
mod task_summaries;
mod usage;
use crate::tasks::io::TaskStore;
use anyhow::{Context, Result};
pub(crate) use events::{Event, EventMessage};
use std::sync::Arc;
pub(crate) use task_summaries::TaskSummaryRequest;
use tokio::sync::{RwLock, broadcast};

pub(crate) struct Services {
    tasks: TaskStore,
    pub(crate) library: LibraryCatalog,
    pub(crate) templates: AgentTemplates,
    pub(crate) music: crate::music::Music,
    usage: RwLock<crate::usage::Snapshot>,
    events: broadcast::Sender<Arc<EventMessage>>,
}

impl Services {
    pub(crate) async fn load(
        data: &std::path::Path,
        config: &std::path::Path,
        tmux: crate::tmux::Tmux,
    ) -> Result<Arc<Self>> {
        let tasks = TaskStore::load(data.to_owned())
            .await
            .with_context(|| format!("loading task store in {}", data.display()))?;
        let templates = AgentTemplates::load(crate::agent_templates::AgentTemplateStore::path_for(
            &config.join("config.toml"),
        ))
        .await?;
        Ok(Arc::new(Self::new(tasks, tmux, 256, templates)))
    }

    pub(crate) fn new(
        tasks: TaskStore,
        tmux: crate::tmux::Tmux,
        event_capacity: usize,
        templates: AgentTemplates,
    ) -> Self {
        let (events, _) = broadcast::channel(event_capacity);
        Self {
            tasks,
            library: LibraryCatalog::default(),
            templates,
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
