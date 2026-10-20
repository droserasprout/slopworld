//! Prepare errand configuration and saved hosts before handing a complete plan to session.
//! The session operation reserves names through persistence and live publication.
use super::launch::Launches;
use crate::config::Config;
use crate::session::LaunchRequest;
use crate::session::{ErrandAllocation, PreparedErrand};
use anyhow::{Result, anyhow};

impl Launches {
    pub(crate) async fn create_errand_session(
        &self,
        cfg: &Config,
        request: &LaunchRequest,
    ) -> Result<String> {
        self.sessions
            .session_operation(self.prepare_and_publish(cfg, request))
            .await
    }

    async fn prepare_and_publish(&self, cfg: &Config, request: &LaunchRequest) -> Result<String> {
        let selection =
            crate::application::errands::select(cfg, request, &self.templates.list().await)?;
        let project_path = if selection.fresh {
            None
        } else {
            let project = cfg
                .project(&selection.project_name)
                .ok_or_else(|| anyhow!("Project {:?} does not exist.", selection.project_name))?;
            Some(
                self.sessions
                    .resolve_worktree(project, &selection.selected_worktree)
                    .await?,
            )
        };
        let slot = match self
            .sessions
            .allocate_errand(
                cfg,
                request,
                &selection.project_name,
                selection.fresh,
                &selection.selected_worktree,
            )
            .await?
        {
            ErrandAllocation::Existing(name) => return Ok(name),
            ErrandAllocation::New(slot) => *slot,
        };
        let source_settings =
            crate::application::errands::capture_source_settings(cfg, selection.source.as_ref())?;
        let host_path = if request.execution.persistent_host() {
            let project = project_path
                .as_ref()
                .ok_or_else(|| anyhow!("Project {:?} does not exist.", selection.project_name))?;
            let path = crate::config::expand(&project.dir);
            self.host_records
                .remember(&self.sessions, &slot.name, &slot.project, &path)
                .await?;
            path
        } else {
            String::new()
        };
        let (mut session, context) = crate::application::errands::build_session(
            cfg,
            &request.content,
            &request.options,
            &slot.name,
            slot.project.clone(),
            selection.template,
            source_settings,
        );
        session.worktree = selection.selected_worktree;
        self.sessions
            .publish_errand(PreparedErrand {
                slot,
                session,
                context,
                host_path,
            })
            .await
    }
}
