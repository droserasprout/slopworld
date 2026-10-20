//! Guarded errand identity allocation, editor reuse, and live publication.
//! Application prepares executable settings and persists host records before publication.
use super::*;

/// An unpublished reservation. The caller retains the exclusive session operation
/// from allocation through publication; dropping it creates no live or temporary state.
pub(crate) struct ErrandSlot {
    pub(crate) name: String,
    pub(crate) project: String,
    temporary_project: Option<ProjectCfg>,
    role: SessionRole,
    dimensions: Option<(u16, u16)>,
}

pub(crate) enum ErrandAllocation {
    Existing(String),
    New(Box<ErrandSlot>),
}

/// Fully prepared configuration and display state; no policy or persistence remains.
pub(crate) struct PreparedErrand {
    pub(crate) slot: ErrandSlot,
    pub(crate) session: SessionCfg,
    pub(crate) context: LaunchContext,
    pub(crate) host_path: String,
}

impl SessionRuntime {
    pub(crate) async fn allocate_errand(
        &self,
        cfg: &Config,
        request: &LaunchRequest,
        project: &str,
        fresh: bool,
        worktree: &str,
    ) -> Result<ErrandAllocation> {
        assert!(
            self.session_write_operation_active(),
            "errand allocation requires session boundary"
        );
        let want = &request.options;
        let role = match request.execution {
            ExecutionSource::Host(HostLifetime::Persistent) => SessionRole::HostShell,
            ExecutionSource::Host(HostLifetime::Disposable) => SessionRole::HostAction,
            ExecutionSource::Template(_) | ExecutionSource::CloneAgent(_) => {
                SessionRole::Agent(AgentLifetime::Temporary)
            }
        };
        let live = self.live.read().await;
        // Serialized editor opens reuse the same running pane, including after adoption.
        if role == SessionRole::HostAction
            && want.context.intent == "edit"
            && !want.context.reader.path.is_empty()
            && let Some(editor) = live.values().find(|row| {
                row.role == SessionRole::HostAction
                    && row.state != State::Down
                    && row.launch.intent == "edit"
                    && row.launch.reader.path == want.context.reader.path
                    && row.launch.reader.scope == want.context.reader.scope
                    && row.cfg.worktree == worktree
                    && (fresh || row.cfg.project == project)
            })
        {
            return Ok(ErrandAllocation::Existing(editor.cfg.name.clone()));
        }
        let name = if role != SessionRole::HostShell && !want.context.intent.is_empty() {
            crate::storage_id::allocate(|id| {
                Ok(live.contains_key(id)
                    || cfg.sessions.iter().any(|session| session.name == id)
                    || cfg.host_terminals.iter().any(|tab| tab.name == id))
            })?
        } else {
            let preferred = if role == SessionRole::HostShell {
                crate::sandbox::host_session_name(project)
            } else {
                slug(&request.content.name)
            };
            free_name(&live, cfg, &preferred)
        };
        check_name(&name)?;
        drop(live);
        let temporary_project = if fresh {
            let temp = self.temp.read().await;
            let project_name = free_project_name(cfg, &temp, &name);
            Some(ProjectCfg {
                dir: crate::paths::temp_dir(&project_name),
                name: project_name,
                temp: true,
                mounts: Vec::new(),
                ..Default::default()
            })
        } else {
            None
        };
        Ok(ErrandAllocation::New(Box::new(ErrandSlot {
            name,
            project: temporary_project
                .as_ref()
                .map_or_else(|| project.to_owned(), |p| p.name.clone()),
            temporary_project,
            role,
            dimensions: want.cols.zip(want.rows),
        })))
    }

    pub(crate) async fn publish_errand(&self, prepared: PreparedErrand) -> Result<String> {
        assert!(
            self.session_write_operation_active(),
            "errand publication requires session boundary"
        );
        let PreparedErrand {
            slot,
            session,
            context,
            host_path,
        } = prepared;
        let mut state = Live::new(session, TitleCapture::default());
        state.launch = context;
        state.role = slot.role;
        state.host_path = host_path;
        // Seed the viewer size before startup; resizing during LESSOPEN can leave blank rows.
        if let Some((cols, rows)) = slot.dimensions {
            state.cols = cols.clamp(
                crate::shared::protocol::TERMINAL_MIN_COLS,
                crate::shared::protocol::TERMINAL_MAX_COLS,
            );
            state.rows = rows.clamp(
                crate::shared::protocol::TERMINAL_MIN_ROWS,
                crate::shared::protocol::TERMINAL_MAX_ROWS,
            );
        }
        let mut live = self.live.write().await;
        if live.contains_key(&slot.name) {
            bail!("session {} already exists", slot.name);
        }
        if let Some(project) = slot.temporary_project {
            // Acquire both locks before either publication. Cancellation leaves neither row.
            let mut temp = self.temp.write().await;
            temp.insert(project.name.clone(), project);
            live.insert(slot.name.clone(), state);
        } else {
            live.insert(slot.name.clone(), state);
        }
        Ok(slot.name)
    }
}

#[cfg(test)]
#[path = "errands_tests.rs"]
mod tests;
