//! Agent-session edits, labels, rename handoffs, and private-state recovery.
//! Host metadata belongs to host_metadata; lifecycle owns launch/stop and views owns projection.

use super::lifecycle::stop::{finish_reader, take_reader_for_abort};
use super::*;
use crate::session::config::ConfigMutation;
use anyhow::anyhow;
// Bound user labels shown in session lists.
const MAX_MANUAL_LABEL_CHARS: usize = 60;

impl SessionRuntime {
    pub(crate) async fn host_sessions(&self) -> Vec<String> {
        self.live
            .read()
            .await
            .iter()
            .filter(|(_, live)| live.role.is_host())
            .map(|(name, _)| name.clone())
            .collect()
    }

    /// Return a sanitized launch report for an authorized diagnostic request.
    /// The sandbox module owns artifact parsing and process observation.
    /// API and CLI callers use this shared path to apply the same safeguards.
    pub(crate) async fn sandbox_inspect(&self, name: &str) -> Result<serde_json::Value> {
        let session = self
            .session_cfg(name)
            .await
            .ok_or_else(|| anyhow!("no such session: {name}"))?;
        let host = self.is_host(name).await;
        crate::sandbox::inspect_session(&self.tmux, name, &session, host).await
    }

    pub(super) async fn session_cfg(&self, name: &str) -> Option<SessionCfg> {
        if let Some(s) = self.config_authority.read_config().await.session(name) {
            return Some(s.clone());
        }
        self.live.read().await.get(name).map(|l| l.cfg.clone())
    }

    pub(super) async fn project_for(&self, cfg: &Config, s: &SessionCfg) -> Option<ProjectCfg> {
        if let Some(p) = cfg.project_of(s) {
            return self.resolve_worktree(p, &s.worktree).await.ok();
        }
        if let Some(p) = self.temp.read().await.get(&s.project).cloned() {
            return Some(p);
        }
        // A host tab can remain after deletion of its project entry.
        // Use its last tmux working directory so the ungrouped tab can still restart.
        let path = self
            .live
            .read()
            .await
            .get(&s.name)
            .filter(|l| l.role.is_host())
            .map(|l| l.host_path.clone())
            .filter(|path| !path.trim().is_empty())?;
        Some(ProjectCfg {
            name: s.project.clone(),
            dir: path,
            ..Default::default()
        })
    }

    async fn session_role(&self, name: &str) -> Option<SessionRole> {
        self.live.read().await.get(name).map(|live| live.role)
    }

    pub(super) async fn is_host(&self, name: &str) -> bool {
        self.live
            .read()
            .await
            .get(name)
            .map(|l| l.role.is_host())
            .unwrap_or(false)
    }

    pub async fn add(self: &Arc<Self>, s: SessionCfg) -> Result<()> {
        let manager = self.clone();
        self.owned_session_operation(async move { Box::pin(manager.add_inner(s, false)).await })
            .await
    }

    pub(crate) async fn add_template_session(self: &Arc<Self>, s: SessionCfg) -> Result<()> {
        let manager = self.clone();
        self.owned_session_operation(async move { Box::pin(manager.add_inner(s, true)).await })
            .await
    }

    async fn add_inner(
        self: &Arc<Self>,
        mut s: SessionCfg,
        preserve_snapshots: bool,
    ) -> Result<()> {
        self.configuration().reload_if_changed().await;
        if let Some(p) = self.config().await.project(&s.project) {
            self.resolve_worktree(p, &s.worktree).await?;
        }
        let state_id = self.allocate_agent_identity().await?;
        let (autostart, name) = self
            .configuration()
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

        self.sync_from_config().await;
        // sync_from_config already attempts autostart for the saved session.
        // Retry only if no tmux session exists. Otherwise, a successful addition would report an already-running error.
        if autostart && !self.tmux.exists(&name).await {
            self.start(&name).await?;
        }
        Ok(())
    }

    pub async fn update(self: &Arc<Self>, name: &str, s: SessionCfg) -> Result<()> {
        // After tmux accepts the new name, finish the transaction even if the caller cancels the HTTP request.
        // This prevents tmux from retaining a name that was never saved.
        // Transfer the request's session boundary to the owned task; acquiring it
        // again in a bare spawned task would deadlock against the waiting request.
        let manager = self.clone();
        let name = name.to_string();
        self.owned_session_operation(async move { Box::pin(manager.update_inner(&name, s)).await })
            .await
    }

    async fn update_inner(self: &Arc<Self>, name: &str, mut s: SessionCfg) -> Result<()> {
        self.configuration().reload_if_changed().await;
        if let Some(p) = self.config().await.project(&s.project) {
            self.resolve_worktree(p, &s.worktree).await?;
        }
        if let Some(old) = self.session_cfg(name).await
            && (old.worktree != s.worktree || old.project != s.project)
            && self.tmux.exists(name).await
        {
            bail!("stop the session before moving it to another worktree");
        }
        let _terminal = self.terminal_boundary(name).write_owned().await;
        let renamed = s.name != name;
        check_name(&s.name)?;
        let new_name = s.name.clone();
        let tmux_running = renamed && self.tmux.exists(name).await;
        if renamed {
            if self.tmux.exists(&new_name).await {
                bail!("session {new_name} already exists in tmux");
            }
            if self.live.read().await.contains_key(&new_name) {
                bail!("session {new_name} already exists");
            }
        }

        let ((), prepared) = self
            .configuration()
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
            self.tmux.rename(name, &new_name).await?;
            if let Err(error) = self.configuration().commit_prepared_cfg(prepared).await {
                return self
                    .recover_failed_tmux_rename(name, &new_name, error)
                    .await;
            }
        } else {
            self.configuration().commit_prepared_cfg(prepared).await?;
        }

        if renamed {
            self.readopt(name, &new_name).await;
        }
        drop(_terminal);
        self.sync_from_config().await;
        Ok(())
    }

    async fn recover_failed_tmux_rename(
        &self,
        old: &str,
        new: &str,
        persist_error: anyhow::Error,
    ) -> Result<()> {
        match self.tmux.rename(new, old).await {
            Ok(()) => Err(anyhow!(
                "Could not persist session rename {old} -> {new}: {persist_error:#}. Restored tmux to {old}."
            )),
            Err(rollback_error) => {
                // Keep configuration and live state at the saved identity.
                // A second failure requires manual recovery. Report observed tmux names without creating unsaved state.
                let identity = self.tmux_identity(old, new).await;
                tracing::error!(
                    old,
                    new,
                    persist_error = %persist_error,
                    rollback_error = %rollback_error,
                    identity = %identity,
                    "session rename recovery could not restore tmux"
                );
                Err(anyhow!(
                    "Could not persist session rename {old} -> {new}: {persist_error:#}. \
                     Could not restore tmux to {old}: {rollback_error:#}. \
                     Actual tmux identity: {identity}. Manual recovery is required."
                ))
            }
        }
    }

    async fn tmux_identity(&self, old: &str, new: &str) -> String {
        match self.tmux.list_checked().await {
            Ok(names) => {
                let old_present = names.iter().any(|name| name == old);
                let new_present = names.iter().any(|name| name == new);
                match (old_present, new_present) {
                    (true, false) => format!("original session {old} is present"),
                    (false, true) => format!("renamed session {new} is present"),
                    (true, true) => format!("both {old} and {new} are present"),
                    (false, false) => format!("neither {old} nor {new} is present"),
                }
            }
            Err(error) => format!("tmux identity is unavailable: {error:#}"),
        }
    }

    /// Set the optional sidebar label without requiring the edit dialog to send read-only session fields.
    /// Store host labels in persistent host-terminal records, although host rows use the temporary-session presentation.
    pub async fn set_label(self: &Arc<Self>, name: &str, label: String) -> Result<()> {
        let manager = self.clone();
        let name = name.to_owned();
        self.owned_session_operation(async move { manager.set_label_inner(&name, label).await })
            .await
    }

    async fn set_label_inner(self: &Arc<Self>, name: &str, label: String) -> Result<()> {
        self.configuration().reload_if_changed().await;
        let label = label.trim().to_string();
        if label.chars().count() > MAX_MANUAL_LABEL_CHARS {
            bail!("label must be at most {MAX_MANUAL_LABEL_CHARS} characters");
        }

        let saved = if label.is_empty() {
            self.live
                .read()
                .await
                .get(name)
                .filter(|row| {
                    !row.launch.intent.is_empty() && !row.launch.original_label.is_empty()
                })
                .map(|row| row.launch.original_label.clone())
        } else {
            Some(label.clone())
        };
        let role = self.session_role(name).await;
        let host = role.is_some_and(SessionRole::is_host);
        if role.is_none_or(|role| !role.is_temporary_agent()) {
            self.configuration()
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

        {
            let mut live = self.live.write().await;
            let Some(live) = live.get_mut(name) else {
                bail!("no such session: {name}");
            };
            live.cfg.label = saved;
            live.title.label_changed(host);
        }
        self.persist_reader_metadata(name).await?;
        self.announce_sessions().await;
        Ok(())
    }

    pub async fn set_reader_pinned(self: &Arc<Self>, name: &str, pinned: bool) -> Result<()> {
        self.session_operation(async {
            {
                let mut live = self.live.write().await;
                let row = live
                    .get_mut(name)
                    .ok_or_else(|| anyhow!("no such session: {name}"))?;
                if row.launch.intent != "view" && row.launch.intent != "diff" {
                    bail!("session {name} is not a pinnable reader");
                }
                row.launch.reader.pinned = pinned;
            }
            self.persist_reader_metadata(name).await?;
            self.announce_sessions().await;
            Ok(())
        })
        .await
    }

    async fn persist_reader_metadata(&self, name: &str) -> Result<()> {
        let live = self.live.read().await;
        let row = live
            .get(name)
            .ok_or_else(|| anyhow!("no such session: {name}"))?;
        let Some(value) = row.launch.metadata(&row.cfg) else {
            return Ok(());
        };
        drop(live);
        self.tmux.set_reader_metadata(name, &value).await
    }

    pub(super) async fn readopt(self: &Arc<Self>, old: &str, new: &str) {
        // Rename already protects the old name. Protect the destination before
        // publishing its row so root resize cannot race replacement attachment.
        let terminal = self.terminal_boundary(new).write_owned().await;
        // The control reader attaches by tmux name. A rename requires a new capture and emulator.
        let (running, reader) = {
            let mut live = self.live.write().await;
            let Some(mut l) = live.remove(old) else {
                return;
            };
            let reader = take_reader_for_abort(&mut l);
            // The input consumer captures its tmux target at startup.
            // Drop its sender so the next key creates a consumer for the new name.
            // Otherwise, later keys would target the old session name.
            l.input.sender = None;
            let running = l.capture.emu.take().is_some();
            l.capture.reader_token = None;
            l.title.label_changed(false);
            self.application
                .title_cache
                .rename_latest(old, new, l.title.title());
            l.cfg.name = new.to_string();
            live.insert(new.to_string(), l);
            (running, reader)
        };
        finish_reader(reader);
        // The cache is keyed by name. The frames under the old one describe a pane that is about to
        // be re-captured against a fresh emulator.
        self.forget_scroll(old);
        self.forget_scroll(new);
        if let Err(error) = self.activity_cache.rename(old, new) {
            tracing::warn!(
                target: "slopd::activity",
                old = %old,
                new = %new,
                %error,
                "could not rename session activity cache"
            );
        }
        if running {
            match self.spawn_reader_with_terminal(new, &terminal).await {
                Ok(true) => {
                    let m = self.clone();
                    let name = new.to_string();
                    tokio::spawn(async move { m.nudge_redraw(&name).await });
                }
                Ok(false) => {}
                Err(error) => tracing::warn!("could not reattach reader for {new}: {error:#}"),
            }
        }
    }

    pub async fn remove(self: &Arc<Self>, name: &str) -> Result<()> {
        let manager = self.clone();
        let name = name.to_owned();
        self.owned_session_operation(async move { manager.remove_inner(&name).await })
            .await
    }

    async fn remove_inner(self: &Arc<Self>, name: &str) -> Result<()> {
        self.configuration().reload_if_changed().await;
        if self.is_host(name).await {
            self.stop(name).await?;

            let saved = self
                .config_authority
                .read_config()
                .await
                .host_terminals
                .iter()
                .any(|tab| tab.name == name);
            if saved {
                self.configuration()
                    .update_cfg(ConfigMutation::HostShells, |cfg| {
                        cfg.host_terminals.retain(|tab| tab.name != name);
                        Ok(())
                    })
                    .await?;
            }

            // Host rows have no private agent state to trash. Forget both durable tabs and
            // unnamed runtime-only host errands after the pane has been stopped.
            self.forget(name).await;
            self.announce_sessions().await;
            return Ok(());
        }
        if self
            .session_role(name)
            .await
            .is_some_and(SessionRole::is_temporary_agent)
        {
            return self.stop(name).await;
        }
        self.stop(name).await?;
        let session = self
            .config_authority
            .read_config()
            .await
            .session(name)
            .cloned()
            .ok_or_else(|| anyhow!("no such session: {name}"))?;
        let trashed = crate::sandbox::trash_state(&session, name)?;
        if let Err(e) = self.remove_configured_session(name).await {
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
        self.forget(name).await;
        self.announce_sessions().await;
        Ok(())
    }

    /// Stop the agent. Move its private tool state to the daemon trash for two weeks of recovery.
    /// Keep the configured agent. Initialize new private state when it starts again.
    pub async fn reset_state(self: &Arc<Self>, name: &str) -> Result<()> {
        self.session_operation(self.reset_state_inner(name)).await
    }

    async fn reset_state_inner(self: &Arc<Self>, name: &str) -> Result<()> {
        self.configuration().reload_if_changed().await;
        if self
            .session_role(name)
            .await
            .is_some_and(|role| !role.is_durable_agent())
        {
            bail!("temporary session {name} has no resettable private state");
        }
        self.stop(name).await?;
        let cfg = self.config().await;
        let session = cfg
            .session(name)
            .cloned()
            .ok_or_else(|| anyhow!("no such session: {name}"))?;
        crate::sandbox::trash_state(&session, &format!("{name}-reset"))?;
        if let Err(e) = crate::sandbox::purge_trash() {
            tracing::warn!("purging private-state trash: {e:#}");
        }
        self.sync_from_config().await;
        Ok(())
    }

    pub async fn restore_stored_state(self: &Arc<Self>, key: &str) -> Result<String> {
        let manager = self.clone();
        let key = key.to_owned();
        self.owned_session_operation(async move {
            Box::pin(manager.restore_stored_state_inner(&key)).await
        })
        .await
    }

    async fn restore_stored_state_inner(self: &Arc<Self>, key: &str) -> Result<String> {
        self.configuration().reload_if_changed().await;
        let archived = crate::sandbox::trashed_session(key)?;
        let cfg = self.config().await;
        let existing = cfg
            .sessions
            .iter()
            .find(|s| !archived.state_id.is_empty() && s.state_id == archived.state_id)
            .cloned();
        let add = existing.is_none();
        let session = existing.unwrap_or_else(|| archived.clone());
        if add {
            check_name(&session.name)?;
        }

        crate::sandbox::restore_stored_state(key, &cfg.sessions)?;
        if add
            && let Err(e) = self
                .configuration()
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
        self.sync_from_config().await;
        Ok(session.name)
    }
}

#[cfg(test)]
#[path = "sessions_tests.rs"]
mod tests;
