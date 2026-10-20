//! Personal template catalog mutations and cancellation-safe persistence/publication.
//! Session adapters own capture and instantiation; crate::agent_templates owns
//! portable definitions, version policy, and the on-disk format.
use crate::agent_templates::{AgentTemplate, AgentTemplateStore};
use anyhow::{Context, Result};
#[cfg(test)]
use std::sync::Mutex;
use std::{path::PathBuf, sync::Arc};
use tokio::sync::RwLock;

#[derive(Debug)]
pub(crate) struct TemplatePersistence;

impl std::fmt::Display for TemplatePersistence {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str("saving agent templates")
    }
}

/// Readers keep the accepted catalog while a draft saves under the mutation gate.
pub(crate) struct AgentTemplates {
    store: Arc<RwLock<AgentTemplateStore>>,
    path: PathBuf,
    mutation: Arc<tokio::sync::Mutex<()>>,
    #[cfg(test)]
    publish_pause: Mutex<Option<(Arc<tokio::sync::Notify>, Arc<tokio::sync::Notify>)>>,
}

impl AgentTemplates {
    pub(crate) async fn load(path: PathBuf) -> Result<Self> {
        let store = AgentTemplateStore::load(&path)
            .await
            .with_context(|| format!("loading agent template store {}", path.display()))?;
        Ok(Self::new(store, path))
    }

    pub(crate) fn new(store: AgentTemplateStore, path: PathBuf) -> Self {
        Self {
            store: Arc::new(RwLock::new(store)),
            path,
            mutation: Arc::new(tokio::sync::Mutex::new(())),
            #[cfg(test)]
            publish_pause: Mutex::new(None),
        }
    }

    pub(crate) async fn list(&self) -> Vec<AgentTemplate> {
        self.store.read().await.templates.clone()
    }

    #[cfg(test)]
    pub(crate) fn store_for_test(&self) -> &RwLock<AgentTemplateStore> {
        &self.store
    }

    /// Add a definition only if its destination is absent.
    /// The daemon assigns the version. Callers cannot select a version for a new definition.
    pub(crate) async fn create(&self, template: AgentTemplate) -> Result<AgentTemplate> {
        self.mutate(|store| store.create(template)).await
    }

    /// Replace a definition after comparing its expected version under the same lock used for
    /// persistence. Existing instantiated agents retain their snapshots.
    pub(crate) async fn replace(
        &self,
        old_name: &str,
        expected_version: u64,
        template: AgentTemplate,
    ) -> Result<AgentTemplate> {
        self.mutate(|store| store.replace(old_name, expected_version, template))
            .await
    }

    pub(crate) async fn duplicate(
        &self,
        source_name: &str,
        name: String,
        description: String,
    ) -> Result<AgentTemplate> {
        let mut copy = self
            .list()
            .await
            .into_iter()
            .find(|template| template.name == source_name)
            .ok_or_else(|| {
                crate::agent_templates::AgentTemplateError::Missing(source_name.into())
            })?;
        copy.name = name;
        copy.description = description.trim().to_string();
        self.create(copy).await
    }

    pub(crate) async fn remove(&self, name: &str, expected_version: u64) -> Result<()> {
        self.mutate(|store| store.remove(name, expected_version))
            .await
    }

    async fn mutate<T>(
        &self,
        mutation: impl FnOnce(&mut AgentTemplateStore) -> Result<T>,
    ) -> Result<T> {
        let _mutation = self.mutation.clone().lock_owned().await;
        let mut store = self.store.read().await.clone();
        let result = mutation(&mut store)?;
        store.validate()?;
        let published = self.store.clone();
        let path = self.path.clone();
        #[cfg(test)]
        let publish_pause = self.publish_pause.lock().unwrap().take();
        // Once prepared, cancellation cannot separate disk commit from publication.
        tokio::spawn(async move {
            let _mutation = _mutation;
            store
                .save(&path)
                .await
                .map_err(|error| error.context(TemplatePersistence))?;
            #[cfg(test)]
            if let Some((reached, release)) = publish_pause {
                reached.notify_one();
                release.notified().await;
            }
            *published.write().await = store;
            Ok::<_, anyhow::Error>(())
        })
        .await??;
        Ok(result)
    }
}

#[cfg(test)]
#[path = "agent_templates_tests.rs"]
mod tests;
