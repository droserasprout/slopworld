//! Session targets, lifecycle, stored state and views.

use super::super::*;
use super::lifecycle::stop::{finish_reader, take_reader_for_abort};
use anyhow::anyhow;
// Bound user labels shown in session lists.
const MAX_MANUAL_LABEL_CHARS: usize = 60;

fn update_host_process(l: &mut Live, command: &str) -> bool {
    let process_running = !crate::sandbox::is_shell_command(command);
    if l.process_running == process_running {
        return false;
    }
    l.process_running = process_running;
    true
}

/// Poll timing and the outstanding tmux metadata job.
#[derive(Default)]
pub(crate) struct HostMetadataPoll {
    // Epoch milliseconds; limits polling across maintenance callers.
    pub(super) checked: AtomicU64,
    // A slow listing must not overlap another or block activity classification.
    task: tokio::sync::Mutex<Option<JoinHandle<()>>>,
}

impl Manager {
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
        if let Some(s) = self.cfg.read().await.session(name) {
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
            .filter(|l| l.host)
            .map(|l| l.host_path.clone())
            .filter(|path| !path.trim().is_empty())?;
        Some(ProjectCfg {
            name: s.project.clone(),
            dir: path,
            ..Default::default()
        })
    }

    pub(super) async fn is_ephemeral(&self, name: &str) -> bool {
        self.live
            .read()
            .await
            .get(name)
            .map(|l| l.ephemeral)
            .unwrap_or(false)
    }

    pub(super) async fn is_host(&self, name: &str) -> bool {
        self.live
            .read()
            .await
            .get(name)
            .map(|l| l.host)
            .unwrap_or(false)
    }

    pub(super) async fn remember_host_terminal(
        &self,
        name: &str,
        project: &str,
        path: &str,
    ) -> Result<()> {
        self.update_cfg(|cfg| {
            if cfg.session(name).is_some() {
                bail!("host terminal {name} conflicts with an agent session");
            }
            if let Some(tab) = cfg.host_terminals.iter_mut().find(|tab| tab.name == name) {
                tab.project = project.to_string();
                tab.path = path.to_string();
            } else {
                let id = crate::storage_id::allocate(|id| {
                    Ok(cfg.host_terminals.iter().any(|tab| tab.id == id))
                })?;
                cfg.host_terminals.push(crate::config::HostTerminalCfg {
                    id,
                    name: name.to_string(),
                    label: None,
                    project: project.to_string(),
                    path: path.to_string(),
                    autostart: true,
                });
            }
            Ok(())
        })
        .await?;
        Ok(())
    }

    pub(super) async fn remember_host_path(&self, name: &str, path: &str) -> bool {
        self.remember_host_path_inner(name, None, path, true).await
    }

    async fn remember_host_path_for_run(&self, name: &str, run_id: u64, path: &str) -> bool {
        self.remember_host_path_inner(name, Some(run_id), path, false)
            .await
    }

    async fn remember_host_path_inner(
        &self,
        name: &str,
        run_id: Option<u64>,
        path: &str,
        sync_tmux: bool,
    ) -> bool {
        if path.trim().is_empty() {
            return false;
        }
        if let Some(run) = run_id {
            let live = self.live.read().await;
            if !live
                .get(name)
                .is_some_and(|l| l.host && l.state != State::Down && l.run_id == run)
            {
                return false;
            }
        }
        let live_changed = {
            let mut live = self.live.write().await;
            live.get_mut(name)
                .filter(|l| {
                    l.host && l.host_path != path && run_id.is_none_or(|run| l.run_id == run)
                })
                .map(|l| {
                    l.host_path = path.to_string();
                    true
                })
                .unwrap_or(false)
        };

        let (project, cfg_changed) = match self
            .update_cfg_if_changed(|cfg| {
                let mut changed = false;
                let mut project = None;
                if let Some(tab) = cfg.host_terminals.iter_mut().find(|tab| tab.name == name) {
                    project = Some(tab.project.clone());
                    if tab.path != path {
                        tab.path = path.to_string();
                        changed = true;
                    }
                }
                Ok(((project, changed), changed))
            })
            .await
        {
            Ok(result) => result,
            Err(error) => {
                tracing::warn!("could not persist host path for {name}: {error:#}");
                (None, false)
            }
        };

        if sync_tmux
            && (live_changed || cfg_changed)
            && let Some(project) = project
            && let Err(error) = self.tmux.set_host_metadata(name, &project, path).await
        {
            tracing::debug!("could not refresh host metadata for {name}: {error:#}");
        }
        live_changed || cfg_changed
    }

    async fn host_metadata_targets(&self) -> Vec<(String, u64)> {
        self.live
            .read()
            .await
            .values()
            .filter(|l| l.host && l.state != State::Down)
            .map(|l| (l.cfg.name.clone(), l.run_id))
            .collect()
    }

    async fn apply_host_metadata(
        &self,
        targets: Vec<(String, u64)>,
        metadata: std::collections::HashMap<String, crate::tmux::HostMetadata>,
    ) {
        let mut changed = false;
        for (name, run_id) in targets {
            let Some(metadata) = metadata.get(&name) else {
                continue;
            };
            if let Some(path) = metadata.path.as_deref() {
                changed |= self.remember_host_path_for_run(&name, run_id, path).await;
            }
            if let Some(command) = metadata.command.as_deref() {
                let mut live = self.live.write().await;
                if let Some(l) = live
                    .get_mut(&name)
                    .filter(|l| l.host && l.state != State::Down && l.run_id == run_id)
                {
                    changed |= update_host_process(l, command);
                }
            }
        }
        if changed {
            self.announce_sessions().await;
        }
    }

    pub(super) async fn start_host_metadata_poll(self: &Arc<Self>) {
        let mut poll = self.host_metadata.task.lock().await;
        if let Some(task) = poll.as_ref()
            && !task.is_finished()
        {
            return;
        }
        if let Some(task) = poll.take() {
            drop(task.await);
        }

        let targets = self.host_metadata_targets().await;
        if targets.is_empty() {
            return;
        }

        let manager = self.clone();
        let tmux = self.tmux.clone();
        *poll = Some(tokio::spawn(async move {
            let started = std::time::Instant::now();
            match tmux.current_host_metadata_all().await {
                Ok(metadata) => {
                    let apply_manager = manager.clone();
                    manager
                        .session_operation(async move {
                            apply_manager.apply_host_metadata(targets, metadata).await;
                        })
                        .await;
                }
                Err(error) => {
                    tracing::debug!(error = %error, "Host metadata poll failed. Retaining known state.");
                }
            }
            tracing::debug!(
                target: "slopd::perf",
                lane = "host-metadata",
                elapsed_us = crate::clock::duration_us(started.elapsed()),
                "host metadata poll"
            );
        }));
    }

    pub async fn add(self: &Arc<Self>, s: SessionCfg) -> Result<()> {
        self.session_operation(self.add_inner(s, false)).await
    }

    pub(super) async fn add_template_session(self: &Arc<Self>, s: SessionCfg) -> Result<()> {
        self.session_operation(self.add_inner(s, true)).await
    }

    async fn add_inner(
        self: &Arc<Self>,
        mut s: SessionCfg,
        preserve_snapshots: bool,
    ) -> Result<()> {
        self.reload_if_changed().await;
        if let Some(p) = self.config().await.project(&s.project) {
            self.resolve_worktree(p, &s.worktree).await?;
        }
        let (autostart, name) = self
            .update_cfg(|cfg| {
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
                s.state_id = uuid::Uuid::new_v4().to_string();
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
        // The detached task holds the session boundary while it commits or restores the previous state.
        // A connected caller waits for the result.
        let manager = self.clone();
        let name = name.to_string();
        tokio::spawn(async move {
            manager
                .session_operation(manager.update_inner(&name, s))
                .await
        })
        .await
        .map_err(|error| anyhow!("session update task failed: {error}"))?
    }

    async fn update_inner(self: &Arc<Self>, name: &str, mut s: SessionCfg) -> Result<()> {
        self.reload_if_changed().await;
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
                bail!("session {} already exists in tmux", new_name);
            }
            if self.live.read().await.contains_key(&new_name) {
                bail!("session {} already exists", new_name);
            }
        }

        let ((), prepared) = self
            .prepare_cfg_change(|cfg| {
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
                    bail!("session {} already exists", new_name);
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
            if let Err(error) = self.commit_prepared_cfg(prepared).await {
                return self
                    .recover_failed_tmux_rename(name, &new_name, error)
                    .await;
            }
        } else {
            self.commit_prepared_cfg(prepared).await?;
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
        self.reload_if_changed().await;
        let label = label.trim().to_string();
        if label.chars().count() > MAX_MANUAL_LABEL_CHARS {
            bail!("label must be at most {MAX_MANUAL_LABEL_CHARS} characters");
        }

        let saved = if label.is_empty() {
            self.live
                .read()
                .await
                .get(name)
                .filter(|row| !row.cfg.intent.is_empty() && !row.cfg.reader_label.is_empty())
                .map(|row| row.cfg.reader_label.clone())
        } else {
            Some(label.clone())
        };
        let host = self.is_host(name).await;
        if host || !self.is_ephemeral(name).await {
            self.update_cfg(|cfg| {
                if host {
                    // Runtime-only host errands have no host_terminal record. Their label is
                    // still useful for the current pane, but there is nothing to persist.
                    if let Some(tab) = cfg.host_terminals.iter_mut().find(|tab| tab.name == name) {
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
            })
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
                if row.cfg.intent != "view" && row.cfg.intent != "diff" {
                    bail!("session {name} is not a pinnable reader");
                }
                row.cfg.reader_pinned = pinned;
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
        if row.cfg.intent.is_empty() {
            return Ok(());
        }
        let value = crate::tmux::ReaderMetadata {
            intent: row.cfg.intent.clone(),
            label: row.cfg.label.clone().unwrap_or_default(),
            original_label: row.cfg.reader_label.clone(),
            project: row.cfg.project.clone(),
            worktree: row.cfg.worktree.clone(),
            path: row.cfg.reader_path.clone(),
            key: row.cfg.reader_key.clone(),
            scope: row.cfg.reader_scope.clone(),
            pinned: row.cfg.reader_pinned,
            line: row.cfg.reader_line,
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
            self.title_cache.rename_latest(old, new, l.title.title());
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
        self.session_operation(self.remove_inner(name)).await
    }

    async fn remove_inner(self: &Arc<Self>, name: &str) -> Result<()> {
        self.reload_if_changed().await;
        if self.is_host(name).await {
            self.stop(name).await?;

            let saved = self
                .cfg
                .read()
                .await
                .host_terminals
                .iter()
                .any(|tab| tab.name == name);
            if saved {
                self.update_cfg(|cfg| {
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
        if self.is_ephemeral(name).await {
            return self.stop(name).await;
        }
        self.stop(name).await?;
        let session = self
            .cfg
            .read()
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
        self.reload_if_changed().await;
        if self.is_ephemeral(name).await {
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

    pub async fn stored_states(
        &self,
        measure_sizes: bool,
    ) -> Result<Vec<crate::sandbox::StoredState>> {
        let cfg = self.config().await;
        tokio::task::spawn_blocking(move || {
            // Quick inventory skips expired-tree deletion as well as recursive accounting.
            if measure_sizes && let Err(e) = crate::sandbox::purge_trash() {
                tracing::warn!("purging private-state trash before inventory: {e:#}");
            }
            let mut entries = crate::sandbox::stored_states(&cfg.sessions, measure_sizes);
            entries.extend(crate::sandbox::cache::inventory(
                &cfg.projects,
                measure_sizes,
            ));
            entries
        })
        .await
        .map_err(|e| anyhow!("scanning private state: {e}"))
    }

    pub async fn empty_trash(&self) -> Result<()> {
        tokio::task::spawn_blocking(crate::sandbox::empty_trash)
            .await
            .map_err(|e| anyhow!("emptying private-state trash: {e}"))??;
        Ok(())
    }

    pub async fn delete_stored_state(&self, kind: &str, key: &str) -> Result<()> {
        let sessions = self.config().await.sessions;
        let kind = kind.to_string();
        let key = key.to_string();
        tokio::task::spawn_blocking(move || {
            crate::sandbox::delete_stored_state(&kind, &key, &sessions)
        })
        .await
        .map_err(|e| anyhow!("deleting private state: {e}"))?
    }

    pub async fn restore_stored_state(self: &Arc<Self>, key: &str) -> Result<String> {
        self.session_operation(self.restore_stored_state_inner(key))
            .await
    }

    async fn restore_stored_state_inner(self: &Arc<Self>, key: &str) -> Result<String> {
        self.reload_if_changed().await;
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
                .update_cfg(|cfg| {
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

#[cfg(test)]
#[path = "removal_bench.rs"]
mod removal_bench;
