//! Session capture and instantiation adapters for the application template catalog.
use super::super::*;

impl Manager {
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

        self.application.templates.create(template).await
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
            .application
            .templates
            .list()
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
}

#[cfg(test)]
#[path = "agent_templates_tests.rs"]
mod tests;
