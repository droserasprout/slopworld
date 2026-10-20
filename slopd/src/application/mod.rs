//! Feature operation entry points and daemon-wide services constructed at startup.
//! Session runtime supplies identity and operation leases; API owns wire encoding.
//! Feature entry points coordinate operations through runtime leases; live validation,
//! state, registration, lifecycle and reconciliation remain with session.
mod agent_templates;
mod configuration;
mod state;
pub(crate) use configuration::Configuration;
pub(crate) use state::Application;
#[cfg(test)]
pub(crate) use state::{test_application, test_application_with_socket};
pub(crate) mod catalogs;
pub(crate) mod file_actions;
pub(crate) mod workers;
pub(crate) mod worktrees;
pub(crate) use agent_templates::{AgentTemplates, TemplatePersistence};
mod workspace;
mod workspace_identity;
pub(crate) use workspace::Workspace;
mod config_authority;
pub(crate) use config_authority::{AuthChange, ConfigAuthority};
pub(crate) mod errands;
mod events;
pub(crate) mod launch;
pub(crate) mod library;
mod music;
pub(crate) mod projects;
pub(crate) mod stored_state;
pub(crate) use library::{LibraryCatalog, LibraryMutation, LibraryRuntime};
mod maintenance;
pub(crate) mod startup;
mod task_summaries;
mod usage;
use crate::tasks::io::TaskStore;
use anyhow::{Context, Result};
pub(crate) use events::{Event, EventMessage};
use std::sync::Arc;
use tokio::sync::{RwLock, broadcast};

pub(crate) struct Services {
    tasks: TaskStore,
    pub(crate) title_cache: Arc<crate::title::SummaryCache>,
    pub(crate) catalogs: catalogs::CatalogState,
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
        let cache = Arc::new(crate::title::SummaryCache::load(crate::title::cache_path(
            &config.join("config.toml"),
        )));
        let mut services = Self::new(tasks, cache, tmux, 256, templates);
        services.catalogs = catalogs::CatalogState::load();
        Ok(Arc::new(services))
    }

    pub(crate) fn new(
        tasks: TaskStore,
        title_cache: Arc<crate::title::SummaryCache>,
        tmux: crate::tmux::Tmux,
        event_capacity: usize,
        templates: AgentTemplates,
    ) -> Self {
        let (events, _) = broadcast::channel(event_capacity);
        Self {
            tasks,
            title_cache,
            catalogs: catalogs::CatalogState::new(None, None),
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

mod template_operations;
pub(crate) use template_operations::TemplateOperations;
