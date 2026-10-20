//! Capture portable templates and register instantiated sessions.
//! The catalog owns persistence; SessionEdits coordinates guarded registration.
use super::{Application, Configuration, Services};
use crate::{agent_templates::AgentTemplate, config::SessionCfg, session::SessionRuntime};
use anyhow::{Result, bail};
use std::sync::Arc;
pub(crate) struct TemplateOperations {
    sessions: Arc<SessionRuntime>,
    edits: super::SessionEdits,
    configuration: Configuration,
    services: Arc<Services>,
}
impl TemplateOperations {
    pub(crate) fn new(application: &Application) -> Self {
        Self {
            sessions: application.sessions.clone(),
            edits: super::SessionEdits::new(application),
            configuration: Configuration::new(application),
            services: application.services.clone(),
        }
    }
}
impl TemplateOperations {
    pub(crate) async fn capture_agent_template(
        &self,
        source_name: &str,
        template_name: String,
        description: String,
    ) -> Result<AgentTemplate> {
        self.configuration.reload_if_changed().await;
        self.sessions
            .session_operation(self.save_agent_template_inner(
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
        let cfg = self.configuration.config().await;
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

        self.services.templates.create(template).await
    }

    /// Create a session from a template. The caller supplies the identity and project.
    /// The optional form supplies portable overrides. Exclude identity, state, hierarchy, credentials, and runtime fields from those overrides.
    /// Resolve project mounts at startup.
    pub(crate) async fn create_from_agent_template(
        &self,
        template_name: &str,
        name: String,
        project: String,
        overrides: Option<SessionCfg>,
        start: Option<bool>,
    ) -> Result<String> {
        let session = self
            .services
            .templates
            .instantiate(template_name, name, project, overrides, start)
            .await?;
        let name = session.name.clone();
        self.edits.add_template_session(session).await?;
        Ok(name)
    }
}

#[cfg(test)]
#[path = "template_operations_tests.rs"]
mod tests;
