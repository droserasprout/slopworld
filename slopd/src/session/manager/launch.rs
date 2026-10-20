//! Guarded launch execution and file-scope adapter; application owns launch planning.
use super::super::*;

impl Manager {
    pub(crate) async fn run_launch(self: &Arc<Self>, request: LaunchRequest) -> Result<String> {
        self.session_operation(self.run_launch_inner(request)).await
    }

    async fn run_launch_inner(self: &Arc<Self>, request: LaunchRequest) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self.config().await;
        request.content.validate()?;
        let session = self.create_errand_session(&cfg, &request).await?;

        if !self.tmux.exists(&session).await
            && let Err(e) = self.start(&session).await
        {
            if !request.execution.persistent_host() {
                self.forget(&session).await;
            }
            return Err(e);
        }

        self.announce_sessions().await;
        self.queue_errand_delivery(&session, &request.content, &request.options)
            .await;

        Ok(session)
    }
}

impl crate::application::launch::Runtime for Arc<Manager> {
    async fn file_action_command(
        &self,
        project: &str,
        worktree: &str,
        path: &str,
        command: &str,
        host: bool,
    ) -> Result<String> {
        Manager::file_action_command(self, project, worktree, path, command, host).await
    }
}
