//! Launch preparation and guarded execution; HTTP owns decoding and error mapping.
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

    pub(crate) async fn plan_explicit_launch(
        self: &Arc<Self>,
        q: ExplicitLaunch,
    ) -> Result<LaunchRequest> {
        if !matches!(q.intent.as_str(), "" | "view" | "edit" | "diff" | "search") {
            bail!("Unknown terminal intent.");
        }
        if matches!(q.intent.as_str(), "view" | "edit" | "search") && q.reader.path.is_empty() {
            bail!("A file reader needs a path.");
        }
        let project = q.project.trim();
        let command = if q.path.trim().is_empty() {
            q.command.trim().to_owned()
        } else {
            self.file_action_command(project, &q.worktree, &q.path, q.command.trim(), q.host)
                .await?
        };
        let command = if q.hold && !q.path.trim().is_empty() {
            crate::files::actions::hold_action_command(command.trim())
        } else {
            command.trim().to_owned()
        };
        if command.is_empty() && q.kind != LibraryItemKind::Shell {
            bail!("Provide a command for this action.");
        }
        if project.is_empty() && !q.temp && !q.host {
            bail!("Choose a project or request a temporary project.");
        }
        let name = match q.label.trim() {
            "" if q.host => crate::sandbox::host_session_name(project),
            "" => "run".to_owned(),
            label => label.to_owned(),
        };
        let content = LaunchContent {
            name,
            kind: q.kind,
            command: (!command.is_empty()).then_some(command),
            text: q.text,
        };
        content.validate()?;
        let persistent = q.host
            && q.kind == LibraryItemKind::Shell
            && !project.is_empty()
            && q.path.trim().is_empty()
            && q.command.trim().is_empty()
            && !q.temp;
        // File actions execute on the host, including those scoped to a project.
        let execution = ExecutionSource::select(
            q.host || !q.path.trim().is_empty(),
            persistent,
            &q.agent_template,
            q.like.trim(),
        )?;
        // Unscoped host readers still need a managed working directory.
        let destination = if q.temp || (q.host && project.is_empty()) {
            LaunchDestination::Temporary
        } else {
            LaunchDestination::Project(project.to_owned())
        };
        Ok(LaunchRequest {
            content,
            execution,
            destination,
            options: LaunchOptions {
                worktree: q.worktree,
                cols: q.cols,
                rows: q.rows,
                random_tips: q.random_tips,
                context: LaunchContext {
                    intent: q.intent,
                    reader: ReaderContext {
                        path: q.reader.path,
                        key: q.reader.key,
                        scope: q.reader.scope,
                        line: q.reader.line,
                        pinned: q.reader.pinned,
                    },
                    ..Default::default()
                },
            },
        })
    }
}

#[cfg(test)]
#[path = "launch_tests.rs"]
mod tests;
