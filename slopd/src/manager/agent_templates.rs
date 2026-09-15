//! Manager ownership for the personal agent-template catalog.

use std::sync::Arc;

use anyhow::{bail, Result};

use super::super::*;

impl Manager {
    pub(crate) async fn agent_templates(&self) -> Vec<AgentTemplate> {
        self.templates.read().await.templates.clone()
    }

    /// Capture a configured agent into the personal catalog. The source is copied immediately;
    /// no live link to its project, preset files, library entries, or private state is retained.
    pub(crate) async fn save_agent_template(
        self: &Arc<Self>,
        source_name: &str,
        template_name: String,
        description: String,
    ) -> Result<()> {
        self.reload_if_changed().await;
        self.session_operation(self.save_agent_template_within_boundary(
            source_name,
            template_name,
            description,
        ))
        .await
    }

    async fn save_agent_template_within_boundary(
        &self,
        source_name: &str,
        template_name: String,
        description: String,
    ) -> Result<()> {
        let cfg = self.config().await;
        let source = cfg
            .session(source_name)
            .cloned()
            .ok_or_else(|| anyhow::anyhow!("no configured agent: {source_name}"))?;
        if source.worker {
            bail!("task-owned worker {source_name} cannot be saved as a template");
        }
        let project = cfg
            .project_of(&source)
            .cloned()
            .ok_or_else(|| anyhow::anyhow!("agent {source_name} has no registered project"))?;
        let template = AgentTemplate::from_session(
            template_name,
            description.trim().to_string(),
            &source,
            &project,
            &cfg,
        )?;

        let persist = self.config_state.persist.lock().await;
        let mut store = self.templates.read().await.clone();
        store.insert(template, false)?;
        store
            .save(&AgentTemplateStore::path_for(&self.cfg_path))
            .await?;
        *self.templates.write().await = store;
        drop(persist);
        Ok(())
    }

    /// Create a session from a template. The caller supplies identity and project; the optional
    /// form payload supplies portable overrides and mount selections, never identity,
    /// state, hierarchy, credentials, or runtime fields into the new session. Explicit mount
    /// selections are validated against the destination configuration like ordinary creation.
    pub(crate) async fn create_from_agent_template(
        self: &Arc<Self>,
        template_name: &str,
        name: String,
        project: String,
        overrides: Option<SessionCfg>,
    ) -> Result<String> {
        let template = self
            .templates
            .read()
            .await
            .get(template_name)
            .cloned()
            .ok_or_else(|| anyhow::anyhow!("no such agent template: {template_name}"))?;
        let mut session = template.instantiate(name, project);
        if let Some(overrides) = overrides {
            template.apply_overrides(&mut session, &overrides);
        }
        let name = session.name.clone();
        self.add_template_session(session).await?;
        Ok(name)
    }

    /// Typed replacement is included with the initial API so the later management UI can use
    /// the same persistence boundary. Existing instantiated agents retain their snapshots.
    pub(crate) async fn save_agent_template_definition(
        &self,
        template: AgentTemplate,
    ) -> Result<()> {
        let persist = self.config_state.persist.lock().await;
        let mut store = self.templates.read().await.clone();
        store.insert(template, true)?;
        store
            .save(&AgentTemplateStore::path_for(&self.cfg_path))
            .await?;
        *self.templates.write().await = store;
        drop(persist);
        Ok(())
    }

    pub(crate) async fn remove_agent_template(&self, name: &str) -> Result<()> {
        let persist = self.config_state.persist.lock().await;
        let mut store = self.templates.read().await.clone();
        store.remove(name)?;
        store
            .save(&AgentTemplateStore::path_for(&self.cfg_path))
            .await?;
        *self.templates.write().await = store;
        drop(persist);
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::config::{
        BreadcrumbSnapshot, Config, LibraryItemCfg, LibraryItemKind, ProjectCfg, SessionCfg,
    };
    use crate::presets::SandboxPreset;

    #[tokio::test]
    async fn template_creation_preserves_and_validates_explicit_mounts() {
        use crate::config::{Mount, MountMode};

        let root =
            std::env::temp_dir().join(format!("slopd-template-mounts-{}", uuid::Uuid::new_v4()));
        std::fs::create_dir_all(&root).unwrap();
        let manager = crate::session::test_manager(Config {
            projects: ["repo", "extra"]
                .into_iter()
                .map(|name| ProjectCfg {
                    name: name.into(),
                    dir: root.to_string_lossy().into_owned(),
                    ..Default::default()
                })
                .collect(),
            sessions: vec![SessionCfg {
                name: "source".into(),
                project: "repo".into(),
                ..Default::default()
            }],
            ..Default::default()
        });
        manager
            .save_agent_template("source", "reviewer".into(), "".into())
            .await
            .unwrap();
        let mut overrides = SessionCfg {
            mounts: vec![
                Mount {
                    project: "repo".into(),
                    mode: MountMode::Ro,
                },
                Mount {
                    project: "extra".into(),
                    mode: MountMode::Rw,
                },
            ],
            ..Default::default()
        };
        manager
            .create_from_agent_template(
                "reviewer",
                "new-agent".into(),
                "repo".into(),
                Some(overrides.clone()),
            )
            .await
            .unwrap();
        assert_eq!(
            manager.config().await.session("new-agent").unwrap().mounts,
            overrides.mounts
        );
        overrides.mounts[1].project = "missing-project".into();
        assert!(manager
            .create_from_agent_template(
                "reviewer",
                "invalid-agent".into(),
                "repo".into(),
                Some(overrides)
            )
            .await
            .is_err());
        assert!(manager.config().await.session("invalid-agent").is_none());
        let _ = std::fs::remove_dir_all(root);
    }

    #[tokio::test]
    async fn saving_a_template_persists_it_separately_from_config() {
        let root =
            std::env::temp_dir().join(format!("slopd-template-test-{}", uuid::Uuid::new_v4()));
        std::fs::create_dir_all(&root).unwrap();
        let manager = crate::session::test_manager(Config {
            projects: vec![ProjectCfg {
                name: "repo".into(),
                dir: root.to_string_lossy().into_owned(),
                ..Default::default()
            }],
            sessions: vec![SessionCfg {
                name: "agent".into(),
                project: "repo".into(),
                ..Default::default()
            }],
            ..Default::default()
        });

        manager
            .save_agent_template("agent", "reviewer".into(), "Review changes".into())
            .await
            .unwrap();
        assert_eq!(manager.agent_templates().await.len(), 1);
        assert!(AgentTemplateStore::path_for(&manager.cfg_path).is_file());
        assert!(!manager.cfg_path.is_file());
        let _ = std::fs::remove_dir_all(root);
    }

    #[tokio::test]
    async fn an_instantiated_template_can_still_be_edited_without_live_dependencies() {
        let root =
            std::env::temp_dir().join(format!("slopd-template-edit-{}", uuid::Uuid::new_v4()));
        std::fs::create_dir_all(&root).unwrap();
        let manager = crate::session::test_manager(Config {
            projects: vec![ProjectCfg {
                name: "repo".into(),
                dir: root.to_string_lossy().into_owned(),
                ..Default::default()
            }],
            library: vec![LibraryItemCfg {
                name: "review".into(),
                kind: LibraryItemKind::Breadcrumb,
                text: "check the diff".into(),
                ..Default::default()
            }],
            sessions: vec![SessionCfg {
                name: "source".into(),
                project: "repo".into(),
                command: "claude".into(),
                sandbox: vec!["captured".into()],
                sandbox_snapshots: vec![SandboxPreset {
                    name: "captured".into(),
                    ..Default::default()
                }],
                breadcrumbs: vec!["review".into()],
                breadcrumb_snapshots: vec![BreadcrumbSnapshot {
                    name: "review".into(),
                    text: "captured prompt".into(),
                }],
                ..Default::default()
            }],
            ..Default::default()
        });

        manager
            .save_agent_template("source", "reviewer".into(), "".into())
            .await
            .unwrap();
        manager
            .create_from_agent_template("reviewer", "new-agent".into(), "repo".into(), None)
            .await
            .unwrap();

        // This is the shape sent by the mod editor. Snapshot fields are not on the wire, so the
        // manager must restore the instance's private definitions before validation.
        let update = SessionCfg {
            name: "new-agent".into(),
            project: "repo".into(),
            command: "claude".into(),
            sandbox: manager
                .config()
                .await
                .session("new-agent")
                .unwrap()
                .sandbox
                .clone(),
            breadcrumbs: vec!["review".into()],
            ..Default::default()
        };
        manager.update("new-agent", update).await.unwrap();
        let session = manager.config().await.session("new-agent").unwrap().clone();
        assert!(!session.sandbox_snapshots.is_empty());
        assert_eq!(session.breadcrumb_snapshots[0].text, "captured prompt");
        let _ = std::fs::remove_dir_all(root);
    }
}
