//! Manager ownership for the personal agent-template catalog.

use std::sync::Arc;

use anyhow::{bail, Result};

use super::super::*;

#[derive(Debug)]
pub(crate) struct TemplatePersistence;

impl std::fmt::Display for TemplatePersistence {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str("saving agent templates")
    }
}

/// Personal catalog and its transaction gate; readers can continue while a draft saves.
pub(crate) struct TemplateStore {
    pub(super) store: Arc<RwLock<AgentTemplateStore>>,
    // Hold through version checks, persistence, and publication to prevent lost edits.
    mutation: Arc<tokio::sync::Mutex<()>>,
    #[cfg(test)]
    publish_pause: Mutex<Option<(Arc<tokio::sync::Notify>, Arc<tokio::sync::Notify>)>>,
}

impl TemplateStore {
    pub(super) fn new(store: AgentTemplateStore) -> Self {
        Self {
            store: Arc::new(RwLock::new(store)),
            mutation: Arc::new(tokio::sync::Mutex::new(())),
            #[cfg(test)]
            publish_pause: Mutex::new(None),
        }
    }
}

impl Manager {
    pub(crate) async fn agent_templates(&self) -> Vec<AgentTemplate> {
        self.templates.store.read().await.templates.clone()
    }

    /// Copy a configured agent into the personal catalog immediately.
    /// Do not retain links to its project, preset files, library entries, or private state.
    #[cfg(test)]
    pub(crate) async fn save_agent_template(
        self: &Arc<Self>,
        source_name: &str,
        template_name: String,
        description: String,
    ) -> Result<AgentTemplate> {
        self.capture_agent_template(source_name, template_name, description)
            .await
    }

    pub(crate) async fn capture_agent_template(
        self: &Arc<Self>,
        source_name: &str,
        template_name: String,
        description: String,
    ) -> Result<AgentTemplate> {
        self.reload_if_changed().await;
        self.session_operation(self.save_agent_template_inner(
            source_name,
            template_name,
            description,
        ))
        .await
    }

    async fn save_agent_template_inner(
        &self,
        source_name: &str,
        template_name: String,
        description: String,
    ) -> Result<AgentTemplate> {
        let cfg = self.config().await;
        let source = cfg
            .session(source_name)
            .cloned()
            .ok_or_else(|| anyhow::anyhow!("no configured agent: {source_name}"))?;
        if source.worker {
            bail!("The daemon cannot save task-owned worker {source_name} as a template.");
        }
        let project = cfg
            .project_of(&source)
            .cloned()
            .ok_or_else(|| anyhow::anyhow!("agent {source_name} has no registered project"))?;
        let template = AgentTemplate::capture(
            template_name,
            description.trim().to_string(),
            &source,
            &project,
            &cfg,
        )?;

        self.create_agent_template_definition(template).await
    }

    /// Create a session from a template. The caller supplies the identity and project.
    /// The optional form supplies portable overrides. Exclude identity, state, hierarchy, credentials, and runtime fields from those overrides.
    /// Resolve project mounts at startup.
    pub(crate) async fn create_from_agent_template(
        self: &Arc<Self>,
        template_name: &str,
        name: String,
        project: String,
        overrides: Option<SessionCfg>,
        start: Option<bool>,
    ) -> Result<String> {
        let template = self
            .agent_templates()
            .await
            .into_iter()
            .find(|template| template.name == template_name)
            .ok_or_else(|| anyhow::anyhow!("no such agent template: {template_name}"))?;
        let mut session = template.instantiate(name, project);
        if let Some(overrides) = overrides {
            template.apply_overrides(&mut session, &overrides);
        }
        if let Some(start) = start {
            session.autostart = start;
        }
        let name = session.name.clone();
        self.add_template_session(session).await?;
        Ok(name)
    }

    /// Add a definition only if its destination is absent.
    /// The daemon assigns the version. Callers cannot select a version for a new definition.
    pub(crate) async fn create_agent_template_definition(
        &self,
        template: AgentTemplate,
    ) -> Result<AgentTemplate> {
        self.mutate_template_store(|store| store.create(template))
            .await
    }

    /// Replace a definition after comparing its expected version under the same lock used for
    /// persistence. Existing instantiated agents retain their snapshots.
    pub(crate) async fn replace_agent_template_definition(
        &self,
        old_name: &str,
        expected_version: u64,
        template: AgentTemplate,
    ) -> Result<AgentTemplate> {
        self.mutate_template_store(|store| store.replace(old_name, expected_version, template))
            .await
    }

    pub(crate) async fn duplicate_agent_template(
        &self,
        source_name: &str,
        name: String,
        description: String,
    ) -> Result<AgentTemplate> {
        let mut copy = self
            .agent_templates()
            .await
            .into_iter()
            .find(|template| template.name == source_name)
            .ok_or_else(|| crate::session::AgentTemplateError::Missing(source_name.into()))?;
        copy.name = name;
        copy.description = description.trim().to_string();
        self.create_agent_template_definition(copy).await
    }

    pub(crate) async fn remove_agent_template(
        &self,
        name: &str,
        expected_version: u64,
    ) -> Result<()> {
        self.mutate_template_store(|store| store.remove(name, expected_version))
            .await
    }

    async fn mutate_template_store<T>(
        &self,
        mutation: impl FnOnce(&mut AgentTemplateStore) -> Result<T>,
    ) -> Result<T> {
        let _mutation = self.templates.mutation.clone().lock_owned().await;
        let mut store = self.templates.store.read().await.clone();
        let result = mutation(&mut store)?;
        store.validate()?;
        let published = self.templates.store.clone();
        let path = AgentTemplateStore::path_for(&self.cfg_path);
        #[cfg(test)]
        let publish_pause = self.templates.publish_pause.lock().unwrap().take();
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
