//! Configured session edits and private-state recovery coordination.
//! Session owns live facts, terminal handoffs and operation guards; configuration owns commits.
use super::{Application, Configuration};
use crate::config::transactions::ConfigMutation;
use crate::config::{Config, SessionCfg, check_belongs};
use crate::session::{SessionRole, SessionRuntime, check_name};
use anyhow::{Result, anyhow, bail};
use std::sync::Arc;

const MAX_MANUAL_LABEL_CHARS: usize = 60;

#[derive(Clone)]
pub(crate) struct SessionEdits {
    sessions: Arc<SessionRuntime>,
    configuration: Configuration,
}

impl SessionEdits {
    pub(crate) fn new(application: &Application) -> Self {
        Self {
            sessions: application.sessions.clone(),
            configuration: Configuration::new(application),
        }
    }

    pub(crate) async fn add(&self, s: SessionCfg) -> Result<()> {
        let manager = self.clone();
        self.sessions
            .owned_session_operation(async move { Box::pin(manager.add_inner(s, false)).await })
            .await
    }

    pub(crate) async fn add_template_session(&self, s: SessionCfg) -> Result<()> {
        let manager = self.clone();
        self.sessions
            .owned_session_operation(async move { Box::pin(manager.add_inner(s, true)).await })
            .await
    }

    async fn add_inner(&self, mut s: SessionCfg, preserve_snapshots: bool) -> Result<()> {
        self.configuration.reload_if_changed().await;
        if let Some(p) = self.configuration.config().await.project(&s.project) {
            self.sessions.resolve_worktree(p, &s.worktree).await?;
        }
        self.check_available_name(&self.configuration.config().await, &s.name)
            .await?;
        let state_id = self.sessions.allocate_agent_identity().await?;
        let (autostart, name) = self
            .configuration
            .update_cfg(ConfigMutation::Agents, |cfg| {
                if cfg.session(&s.name).is_some() {
                    bail!("session {} already exists", s.name);
                }
                check_name(&s.name)?;
                if !preserve_snapshots {
                    s.command_snapshot = None;
                    s.sandbox_snapshots.clear();
                }
                check_belongs(cfg, &s)?;
                s.limits.validate()?;
                crate::runtime::validate_limits(&s.limits)?;
                // The daemon owns worker identity. Only spawn_worker can create a child session.
                // Ignore hierarchy fields supplied through the ordinary session editor.
                s.worker = false;
                s.parent.clear();
                s.task_id.clear();
                // Clients cannot select an agent's persistent state identity.
                // Generate a new key even if the request supplies an old one.
                s.state_id = state_id;
                let autostart = s.autostart;
                let name = s.name.clone();
                cfg.sessions.push(s.clone());
                Ok((autostart, name))
            })
            .await?;

        self.sessions.sync_from_config().await;
        // sync_from_config already attempts autostart for the saved session.
        // Retry only if no tmux session exists. Otherwise, a successful addition would report an already-running error.
        if autostart && !self.sessions.tmux.exists(&name).await {
            self.sessions.start(&name).await?;
        }
        Ok(())
    }

    /// Called under the exclusive session operation guard, before state or record changes.
    /// Saved stopped shells, unpublished live identities and surviving tmux panes all reserve names.
    async fn check_available_name(&self, cfg: &Config, name: &str) -> Result<()> {
        check_name(name)?;
        if cfg.session(name).is_some()
            || cfg.host_terminals.iter().any(|host| host.name == name)
            || self.sessions.live_name_exists(name).await
        {
            bail!("session {name} already exists");
        }
        if self.sessions.tmux.exists(name).await {
            bail!("session {name} already exists in tmux");
        }
        Ok(())
    }

    pub(crate) async fn update(&self, name: &str, s: SessionCfg) -> Result<()> {
        // After tmux accepts the new name, finish the transaction even if the caller cancels the HTTP request.
        // This prevents tmux from retaining a name that was never saved.
        // Transfer the request's session boundary to the owned task; acquiring it
        // again in a bare spawned task would deadlock against the waiting request.
        let manager = self.clone();
        let name = name.to_string();
        self.sessions
            .owned_session_operation(async move { Box::pin(manager.update_inner(&name, s)).await })
            .await
    }

    async fn update_inner(&self, name: &str, mut s: SessionCfg) -> Result<()> {
        self.configuration.reload_if_changed().await;
        if let Some(p) = self.configuration.config().await.project(&s.project) {
            self.sessions.resolve_worktree(p, &s.worktree).await?;
        }
        if let Some(old) = self.sessions.session_cfg(name).await
            && (old.worktree != s.worktree || old.project != s.project)
            && self.sessions.tmux.exists(name).await
        {
            bail!("stop the session before moving it to another worktree");
        }
        let _terminal = self.sessions.terminal_boundary(name).write_owned().await;
        let renamed = s.name != name;
        check_name(&s.name)?;
        let new_name = s.name.clone();
        let tmux_running = renamed && self.sessions.tmux.exists(name).await;
        if renamed {
            if self.sessions.tmux.exists(&new_name).await {
                bail!("session {new_name} already exists in tmux");
            }
            if self.sessions.live_name_exists(&new_name).await {
                bail!("session {new_name} already exists");
            }
        }

        let ((), prepared) = self
            .configuration
            .prepare_cfg_change(ConfigMutation::Agents, |cfg| {
                let idx = cfg
                    .sessions
                    .iter()
                    .position(|x| x.name == name)
                    .ok_or_else(|| anyhow!("no such session: {name}"))?;
                if cfg
                    .sessions
                    .get(idx)
                    .ok_or_else(|| anyhow!("no such session: {name}"))?
                    .worker
                {
                    bail!("Task-owned worker {name} cannot be edited. Retry its task instead.");
                }
                if renamed
                    && (cfg
                        .sessions
                        .iter()
                        .enumerate()
                        .any(|(other, session)| other != idx && session.name == new_name)
                        || cfg
                            .host_terminals
                            .iter()
                            .any(|terminal| terminal.name == new_name))
                {
                    bail!("session {new_name} already exists");
                }

                let previous = cfg
                    .sessions
                    .get(idx)
                    .ok_or_else(|| anyhow!("no such session: {name}"))?
                    .clone();
                s.preserve_selected_snapshots(&previous);
                check_belongs(cfg, &s)?;
                s.limits.validate()?;
                crate::runtime::validate_limits(&s.limits)?;
                // Keep daemon-owned worker identity across an ordinary settings edit. The mod's
                // write model intentionally does not expose these fields.
                s.worker = previous.worker;
                s.parent = previous.parent;
                s.task_id = previous.task_id;
                // Preserve the agent's private state identity during all edits, including renames.
                // The protocol does not expose this field and must not permit changes to it.
                s.state_id = previous.state_id;
                let session = cfg
                    .sessions
                    .get_mut(idx)
                    .ok_or_else(|| anyhow!("no such session: {name}"))?;
                *session = s.clone();
                Ok(((), true))
            })
            .await?;

        let Some(prepared) = prepared else {
            return Ok(());
        };

        if tmux_running {
            self.sessions.tmux.rename(name, &new_name).await?;
            if let Err(error) = self.configuration.commit_prepared_cfg(prepared).await {
                return self
                    .sessions
                    .recover_failed_tmux_rename(name, &new_name, error)
                    .await;
            }
        } else {
            self.configuration.commit_prepared_cfg(prepared).await?;
        }

        if renamed {
            self.sessions.readopt(name, &new_name).await;
        }
        drop(_terminal);
        self.sessions.sync_from_config().await;
        Ok(())
    }

    /// Set the optional sidebar label without requiring the edit dialog to send read-only session fields.
    /// Store host labels in persistent host-terminal records, although host rows use the temporary-session presentation.
    pub(crate) async fn set_label(&self, name: &str, label: String) -> Result<()> {
        let manager = self.clone();
        let name = name.to_owned();
        self.sessions
            .owned_session_operation(async move { manager.set_label_inner(&name, label).await })
            .await
    }

    async fn set_label_inner(&self, name: &str, label: String) -> Result<()> {
        self.configuration.reload_if_changed().await;
        let label = label.trim().to_string();
        if label.chars().count() > MAX_MANUAL_LABEL_CHARS {
            bail!("label must be at most {MAX_MANUAL_LABEL_CHARS} characters");
        }

        let saved = if label.is_empty() {
            self.sessions.label_fallback(name).await
        } else {
            Some(label.clone())
        };
        let role = self.sessions.session_role(name).await;
        let host = role.is_some_and(SessionRole::is_host);
        if role.is_none_or(|role| !role.is_temporary_agent()) {
            self.configuration
                .update_cfg(
                    if host {
                        ConfigMutation::HostShells
                    } else {
                        ConfigMutation::Agents
                    },
                    |cfg| {
                        if host {
                            // Runtime-only host errands have no host_terminal record. Their label is
                            // still useful for the current pane, but there is nothing to persist.
                            if let Some(tab) =
                                cfg.host_terminals.iter_mut().find(|tab| tab.name == name)
                            {
                                tab.label = saved.clone();
                            }
                        } else {
                            let session = cfg
                                .sessions
                                .iter_mut()
                                .find(|session| session.name == name)
                                .ok_or_else(|| anyhow!("no such session: {name}"))?;
                            session.label = saved.clone();
                        }
                        Ok(())
                    },
                )
                .await?;
        }

        self.sessions.apply_label(name, saved).await?;
        Ok(())
    }

    pub(crate) async fn remove(&self, name: &str) -> Result<()> {
        let manager = self.clone();
        let name = name.to_owned();
        self.sessions
            .owned_session_operation(async move { manager.remove_inner(&name).await })
            .await
    }

    async fn remove_inner(&self, name: &str) -> Result<()> {
        self.configuration.reload_if_changed().await;
        if self.sessions.is_host(name).await {
            self.sessions.stop(name).await?;

            let saved = self
                .configuration
                .config()
                .await
                .host_terminals
                .iter()
                .any(|tab| tab.name == name);
            if saved {
                self.configuration
                    .update_cfg(ConfigMutation::HostShells, |cfg| {
                        cfg.host_terminals.retain(|tab| tab.name != name);
                        Ok(())
                    })
                    .await?;
            }

            // Host rows have no private agent state to trash. Forget both durable tabs and
            // unnamed runtime-only host errands after the pane has been stopped.
            self.sessions.forget(name).await;
            self.sessions.announce_sessions().await;
            return Ok(());
        }
        // The operation guard excludes starts through deletion. Inspect tmux rather
        // than UI activity: a running pane can temporarily have no capture reader.
        if self.sessions.tmux.exists(name).await
            && self.sessions.tmux.pane_exit(name).await?.is_none()
        {
            bail!("Agent {name} is running. Stop it before deleting it.");
        }
        if self
            .sessions
            .session_role(name)
            .await
            .is_some_and(SessionRole::is_temporary_agent)
        {
            return self.sessions.stop(name).await;
        }
        self.sessions.stop(name).await?;
        let session = self
            .configuration
            .config()
            .await
            .session(name)
            .cloned()
            .ok_or_else(|| anyhow!("no such session: {name}"))?;
        let trashed = crate::sandbox::trash_state(&session, name)?;
        if let Err(e) = self.configuration.remove_agent_record(name).await {
            if let Some(path) = trashed.as_deref()
                && let Err(restore) = crate::sandbox::restore_trashed_state(&session, path)
            {
                tracing::error!(
                    "Config deletion failed: {e:#}. Restoring private state also failed: {restore:#}."
                );
            }
            return Err(e);
        }
        if let Err(e) = crate::sandbox::purge_trash() {
            tracing::warn!("purging private-state trash: {e:#}");
        }
        // This transaction removes only this row. A full reconciliation would
        // probe and potentially restart every unrelated session for each deletion.
        self.sessions.forget(name).await;
        self.sessions.announce_sessions().await;
        Ok(())
    }

    /// Stop the agent. Move its private tool state to the daemon trash for two weeks of recovery.
    /// Keep the configured agent. Initialize new private state when it starts again.
    pub(crate) async fn reset_state(&self, name: &str) -> Result<()> {
        self.sessions
            .session_operation(self.reset_state_inner(name))
            .await
    }

    async fn reset_state_inner(&self, name: &str) -> Result<()> {
        self.configuration.reload_if_changed().await;
        if self
            .sessions
            .session_role(name)
            .await
            .is_some_and(|role| !role.is_durable_agent())
        {
            bail!("temporary session {name} has no resettable private state");
        }
        self.sessions.stop(name).await?;
        let cfg = self.configuration.config().await;
        let session = cfg
            .session(name)
            .cloned()
            .ok_or_else(|| anyhow!("no such session: {name}"))?;
        crate::sandbox::trash_state(&session, &format!("{name}-reset"))?;
        if let Err(e) = crate::sandbox::purge_trash() {
            tracing::warn!("purging private-state trash: {e:#}");
        }
        self.sessions.sync_from_config().await;
        Ok(())
    }

    pub(crate) async fn restore_stored_state(&self, key: &str) -> Result<String> {
        let manager = self.clone();
        let key = key.to_owned();
        self.sessions
            .owned_session_operation(async move {
                Box::pin(manager.restore_stored_state_inner(&key)).await
            })
            .await
    }

    async fn restore_stored_state_inner(&self, key: &str) -> Result<String> {
        self.configuration.reload_if_changed().await;
        let archived = crate::sandbox::trashed_session(key)?;
        let cfg = self.configuration.config().await;
        let existing = cfg
            .sessions
            .iter()
            .find(|s| !archived.state_id.is_empty() && s.state_id == archived.state_id)
            .cloned();
        let add = existing.is_none();
        let session = existing.unwrap_or_else(|| archived.clone());
        if add {
            self.check_available_name(&cfg, &session.name).await?;
        }

        crate::sandbox::restore_stored_state(key, &cfg.sessions)?;
        if add
            && let Err(e) = self
                .configuration
                .update_cfg(ConfigMutation::Agents, |cfg| {
                    if cfg.session(&session.name).is_some() {
                        bail!(
                            "session {:?} already exists with different private state",
                            session.name
                        );
                    }
                    check_belongs(cfg, &session)?;
                    cfg.sessions.push(session.clone());
                    Ok(())
                })
                .await
        {
            if let Err(rollback) = crate::sandbox::rollback_restored_state(key, &session) {
                tracing::error!(
                    "Saving the restored agent config failed: {e:#}. Rolling back state also failed: {rollback:#}."
                );
            }
            return Err(e);
        }
        crate::sandbox::finish_restored_state(&session)?;
        self.sessions.sync_from_config().await;
        Ok(session.name)
    }
}

#[cfg(test)]
#[path = "session_edits_tests.rs"]
mod tests;
