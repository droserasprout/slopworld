//! Session targets, lifecycle, stored state and views.

use super::super::*;
use super::session_lifecycle::{finish_reader, take_reader_for_abort};
use anyhow::anyhow;
fn update_host_process(l: &mut Live, command: &str) -> bool {
    let process_running = !crate::sandbox::is_shell_command(command);
    if l.process_running == process_running {
        return false;
    }
    l.process_running = process_running;
    true
}

impl Manager {
    /// Return a sanitized launch report for an authorized diagnostic request. The sandbox module
    /// owns both artifact parsing and process observation so API and CLI cannot accidentally grow
    /// a second, less careful rendering path.
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
            return Some(p.clone());
        }
        if let Some(p) = self.temp.read().await.get(&s.project).cloned() {
            return Some(p);
        }
        // A host tab may outlive the project entry that created it. Its last tmux cwd is
        // still a safe host starting point, so keep the tab usable and ungrouped instead of
        // making it impossible to restart.
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
                cfg.host_terminals.push(crate::config::HostTerminalCfg {
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

        if sync_tmux && (live_changed || cfg_changed) {
            if let Some(project) = project {
                if let Err(error) = self.tmux.set_host_metadata(name, &project, path).await {
                    tracing::debug!("could not refresh host metadata for {name}: {error:#}");
                }
            }
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
        let mut poll = self.host_metadata_poll.lock().await;
        if let Some(task) = poll.as_ref() {
            if !task.is_finished() {
                return;
            }
        }
        if let Some(task) = poll.take() {
            let _ = task.await;
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
                    tracing::debug!(error = %error, "host metadata poll failed; retaining known state");
                }
            }
            tracing::debug!(
                target: "slopd::perf",
                lane = "host-metadata",
                elapsed_us = started.elapsed().as_micros() as u64,
                "host metadata poll"
            );
        }));
    }

    pub async fn add(self: &Arc<Self>, s: SessionCfg) -> Result<()> {
        self.session_operation(self.add_within_boundary(s, false))
            .await
    }

    pub(super) async fn add_template_session(self: &Arc<Self>, s: SessionCfg) -> Result<()> {
        self.session_operation(self.add_within_boundary(s, true))
            .await
    }

    async fn add_within_boundary(
        self: &Arc<Self>,
        mut s: SessionCfg,
        preserve_snapshots: bool,
    ) -> Result<()> {
        self.reload_if_changed().await;
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
                // Worker identity is daemon-owned. The ordinary session editor cannot create a child by
                // smuggling hierarchy fields through this route; `spawn_worker` is the only constructor.
                s.worker = false;
                s.parent.clear();
                s.task_id.clear();
                // A client has no authority over which durable state an agent receives. Always mint
                // a fresh key, including if a hand-written request carried a stale one.
                s.state_id = uuid::Uuid::new_v4().to_string();
                let autostart = s.autostart;
                let name = s.name.clone();
                cfg.sessions.push(s.clone());
                Ok((autostart, name))
            })
            .await?;

        self.sync_from_config().await;
        // sync_from_config already attempts autostart for the newly persisted session. Retry
        // only when that attempt left no tmux session; otherwise this second start would turn a
        // successful add into an "already running" error.
        if autostart && !self.tmux.exists(&name).await {
            self.start(&name).await?;
        }
        Ok(())
    }

    pub async fn update(self: &Arc<Self>, name: &str, s: SessionCfg) -> Result<()> {
        // Once tmux has accepted the new name, the rest of the transaction must not be
        // cancellable: dropping the HTTP future cannot strand tmux under a name that was never
        // persisted. The detached task still owns the session boundary while it commits or
        // rolls back, and its result is awaited when the caller remains interested.
        let manager = self.clone();
        let name = name.to_string();
        tokio::spawn(async move {
            manager
                .session_operation(manager.update_within_boundary(&name, s))
                .await
        })
        .await
        .map_err(|error| anyhow!("session update task failed: {error}"))?
    }

    async fn update_within_boundary(self: &Arc<Self>, name: &str, mut s: SessionCfg) -> Result<()> {
        self.reload_if_changed().await;
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
                if cfg.sessions[idx].worker {
                    bail!("task-owned worker {name} cannot be edited; retry its task instead");
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

                let previous = cfg.sessions[idx].clone();
                s.preserve_selected_snapshots(&previous);
                check_belongs(cfg, &s)?;
                s.limits.validate()?;
                crate::runtime::validate_limits(&s.limits)?;
                // Keep daemon-owned worker identity across an ordinary settings edit. The mod's
                // write model intentionally does not expose these fields.
                s.worker = previous.worker;
                s.parent = previous.parent;
                s.task_id = previous.task_id;
                // Keep private state with the agent across every edit, particularly a rename. The
                // wire deliberately does not expose this field, but also must not be able to change it.
                s.state_id = previous.state_id;
                cfg.sessions[idx] = s.clone();
                Ok(((), true))
            })
            .await?;

        let Some(prepared) = prepared else {
            unreachable!("session update always changes its candidate")
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
                "could not persist session rename {old} -> {new}: {persist_error:#}; tmux was restored to {old}"
            )),
            Err(rollback_error) => {
                // Keep config/live state at the persisted identity. A second failure requires
                // manual recovery; report observed tmux names rather than inventing unsaved state.
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
                    "could not persist session rename {old} -> {new}: {persist_error:#}; \
                     rollback to {old} also failed: {rollback_error:#}; \
                     actual tmux identity: {identity}; manual recovery required"
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

    /// Set the optional manual sidebar label without making the edit dialog round-trip
    /// read-only session fields. Host labels live in the durable host-terminal record even
    /// though host rows use the ephemeral presentation internally.
    pub async fn set_label(self: &Arc<Self>, name: &str, label: String) -> Result<()> {
        self.reload_if_changed().await;
        let label = label.trim().to_string();
        if label.chars().count() > MAX_MANUAL_LABEL_CHARS {
            bail!("label must be at most {MAX_MANUAL_LABEL_CHARS} characters");
        }

        let saved = (!label.is_empty()).then_some(label.clone());
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
            live.title.generation = live.title.generation.wrapping_add(1);
            live.title.pending = false;
            if host {
                live.title.composer = Composer::ready();
                live.title.override_title = None;
            }
        }
        self.announce_sessions().await;
        Ok(())
    }

    pub(super) async fn readopt(self: &Arc<Self>, old: &str, new: &str) {
        // The control reader is attached by tmux name; renaming requires a fresh capture/emulator.
        let (running, title, reader) = {
            let mut live = self.live.write().await;
            let mut l = match live.remove(old) {
                Some(l) => l,
                None => return,
            };
            let reader = take_reader_for_abort(&mut l);
            // The input consumer captures the tmux target when it is spawned. Drop its
            // sender so the next key creates a consumer addressed to the new name; keeping
            // it would silently route every later key to the vanished old session.
            l.input = None;
            let running = l.emu.take().is_some();
            l.reader_token = None;
            let title = l.title.override_title.clone();
            l.cfg.name = new.to_string();
            live.insert(new.to_string(), l);
            (running, title, reader)
        };
        finish_reader(reader);
        // The cache is keyed by name, and the frames under the old one describe a pane that
        // is about to be re-captured against a fresh emulator.
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
        self.clear_latest_title(old);
        if let Some(t) = title {
            let _ = self.title_cache.remember(new, &t);
        }
        if running {
            match self.spawn_reader(new).await {
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
        self.session_operation(self.remove_within_boundary(name))
            .await
    }

    async fn remove_within_boundary(self: &Arc<Self>, name: &str) -> Result<()> {
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
            .config()
            .await
            .session(name)
            .cloned()
            .ok_or_else(|| anyhow!("no such session: {name}"))?;
        let trashed = crate::sandbox::trash_state(&session, name)?;
        if let Err(e) = self
            .update_cfg(|cfg| {
                cfg.sessions.retain(|s| s.name != name);
                Ok(())
            })
            .await
        {
            if let Some(path) = trashed.as_deref() {
                if let Err(restore) = crate::sandbox::restore_trashed_state(&session, path) {
                    tracing::error!("config delete failed: {e:#}; private-state restore also failed: {restore:#}");
                }
            }
            return Err(e);
        }
        if let Err(e) = crate::sandbox::purge_trash() {
            tracing::warn!("purging private-state trash: {e:#}");
        }
        self.sync_from_config().await;
        Ok(())
    }

    /// Stop an agent and discard only its private tool state.  The old tree is recoverable in
    /// the daemon-owned trash for two weeks; the configured agent remains and reseeds on start.
    pub async fn reset_state(self: &Arc<Self>, name: &str) -> Result<()> {
        self.session_operation(self.reset_state_within_boundary(name))
            .await
    }

    async fn reset_state_within_boundary(self: &Arc<Self>, name: &str) -> Result<()> {
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

    pub async fn stored_states(&self) -> Result<Vec<crate::sandbox::StoredState>> {
        let sessions = self.config().await.sessions;
        tokio::task::spawn_blocking(move || crate::sandbox::stored_states(&sessions))
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
        self.session_operation(self.restore_stored_state_within_boundary(key))
            .await
    }

    async fn restore_stored_state_within_boundary(self: &Arc<Self>, key: &str) -> Result<String> {
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
        if add {
            if let Err(e) = self
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
                    tracing::error!("restored-agent config save failed: {e:#}; state rollback also failed: {rollback:#}");
                }
                return Err(e);
            }
        }
        crate::sandbox::finish_restored_state(&session)?;
        self.sync_from_config().await;
        Ok(session.name)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    async fn rename_fixture(running: bool) -> (Arc<Manager>, std::path::PathBuf, String) {
        let root = std::env::temp_dir().join(format!(
            "slopd-session-rename-{}-{}",
            std::process::id(),
            uuid::Uuid::new_v4()
        ));
        std::fs::create_dir_all(&root).unwrap();
        let socket = format!("slopd-session-rename-{}", uuid::Uuid::new_v4());
        let manager = crate::session::test_manager_with_socket(
            Config {
                projects: vec![ProjectCfg {
                    name: "repo".into(),
                    dir: root.to_string_lossy().into_owned(),
                    ..Default::default()
                }],
                sessions: vec![SessionCfg {
                    name: "old".into(),
                    project: "repo".into(),
                    ..Default::default()
                }],
                ..Default::default()
            },
            socket.clone(),
        );
        manager.update_cfg(|_| Ok(())).await.unwrap();
        if running {
            manager
                .tmux
                .spawn(
                    "old",
                    &root.to_string_lossy(),
                    80,
                    24,
                    &["cat".into()],
                    false,
                )
                .await
                .unwrap();
        }
        let session = manager.config().await.sessions[0].clone();
        manager
            .live
            .write()
            .await
            .insert("old".into(), Live::new(session, TitleCapture::default()));
        if running {
            assert!(manager.spawn_reader("old").await.unwrap());
            assert_rename_input(&manager, "old", "before-rename").await;
        }
        (manager, root, socket)
    }

    async fn assert_rename_input(manager: &Arc<Manager>, name: &str, marker: &str) {
        manager
            .queue_input(name, Input::Bytes(format!("{marker}\n").into_bytes()))
            .await;
        tokio::time::timeout(Duration::from_secs(5), async {
            loop {
                if manager
                    .live
                    .read()
                    .await
                    .get(name)
                    .is_some_and(|live| live.plain.contains(marker))
                {
                    break;
                }
                tokio::time::sleep(Duration::from_millis(10)).await;
            }
        })
        .await
        .expect("input did not reach the managed capture reader");
    }

    async fn cleanup_rename_fixture(
        manager: &Arc<Manager>,
        root: std::path::PathBuf,
        socket: &str,
        names: &[&str],
    ) {
        for name in names {
            if manager.tmux.exists(name).await {
                let _ = manager.tmux.kill(name).await;
            }
        }
        let _ = std::process::Command::new("tmux")
            .args(["-L", socket, "kill-server"])
            .output();
        let _ = std::fs::remove_dir_all(root);
        let _ = std::fs::remove_dir_all(manager.cfg_path.parent().unwrap());
    }

    fn replacement(name: &str) -> SessionCfg {
        SessionCfg {
            name: name.into(),
            project: "repo".into(),
            ..Default::default()
        }
    }

    fn project(name: &str, dir: &std::path::Path, temp: bool) -> ProjectCfg {
        ProjectCfg {
            name: name.into(),
            dir: dir.to_string_lossy().into_owned(),
            temp,
            ..Default::default()
        }
    }

    #[test]
    fn project_directory_must_exist_unless_it_is_temporary() {
        let root = std::env::temp_dir().join(format!(
            "slopd-project-validation-{}-{}",
            std::process::id(),
            uuid::Uuid::new_v4()
        ));
        let missing = root.join("missing");

        let error = Manager::validate_dir(&project("ordinary", &missing, false))
            .unwrap_err()
            .to_string();
        assert!(error.contains("is not a directory"), "{error}");

        assert_eq!(
            Manager::validate_dir(&project("temporary", &missing, true)).unwrap(),
            missing.to_string_lossy()
        );
        assert!(missing.is_dir());
        std::fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn project_directory_rejects_a_protected_ancestor() {
        let error = Manager::validate_dir(&project("world", std::path::Path::new("/"), false))
            .unwrap_err()
            .to_string();
        assert!(error.contains("the whole filesystem"), "{error}");
    }

    #[tokio::test]
    async fn invalid_or_conflicting_renames_do_not_touch_tmux_or_config() {
        let (manager, root, socket) = rename_fixture(true).await;
        manager
            .update_cfg(|cfg| {
                cfg.sessions.push(replacement("occupied"));
                Ok(())
            })
            .await
            .unwrap();
        let persisted = std::fs::read(&manager.cfg_path).unwrap();
        for invalid in [
            replacement("occupied"),
            SessionCfg {
                name: "renamed".into(),
                project: "missing".into(),
                ..Default::default()
            },
            SessionCfg {
                name: "renamed".into(),
                project: "repo".into(),
                sandbox: vec!["missing-preset".into()],
                ..Default::default()
            },
            SessionCfg {
                name: "renamed".into(),
                project: "repo".into(),
                limits: crate::config::Limits {
                    memory_mb: Some(0),
                    ..Default::default()
                },
                ..Default::default()
            },
        ] {
            assert!(manager.update("old", invalid).await.is_err());
            assert!(manager.tmux.exists("old").await);
            assert!(!manager.tmux.exists("renamed").await);
            assert_eq!(manager.config().await.sessions[0].name, "old");
            assert_eq!(manager.config().await.sessions[1].name, "occupied");
            assert!(manager.live.read().await.contains_key("old"));
            assert_eq!(std::fs::read(&manager.cfg_path).unwrap(), persisted);
        }
        cleanup_rename_fixture(&manager, root, &socket, &["old", "renamed"]).await;
    }

    #[tokio::test]
    async fn running_and_down_renames_preserve_private_state() {
        for running in [false, true] {
            let (manager, root, socket) = rename_fixture(running).await;
            let state_id = manager.config().await.sessions[0].state_id.clone();
            manager.update("old", replacement("renamed")).await.unwrap();

            assert!(!manager.tmux.exists("old").await);
            assert_eq!(manager.tmux.exists("renamed").await, running);
            if running {
                assert_rename_input(&manager, "renamed", "after-rename").await;
            }
            let cfg = manager.config().await;
            assert_eq!(cfg.sessions[0].name, "renamed");
            assert_eq!(cfg.sessions[0].state_id, state_id);
            let saved = Config::load(&manager.cfg_path).await.unwrap();
            assert_eq!(saved.sessions[0].name, "renamed");
            assert_eq!(saved.sessions[0].state_id, state_id);
            assert!(manager.live.read().await.contains_key("renamed"));
            assert!(!manager.live.read().await.contains_key("old"));
            cleanup_rename_fixture(&manager, root, &socket, &["old", "renamed"]).await;
        }
    }

    #[tokio::test]
    async fn persistence_failure_restores_tmux_or_reports_failed_rollback() {
        for rollback_fails in [false, true] {
            let (manager, root, socket) = rename_fixture(true).await;
            let persisted = std::fs::read(&manager.cfg_path).unwrap();
            std::fs::create_dir_all(manager.cfg_path.with_extension("toml.tmp")).unwrap();
            if rollback_fails {
                manager.tmux.fail_rename_call_for_test(2);
            }

            let error = manager
                .update("old", replacement("renamed"))
                .await
                .unwrap_err()
                .to_string();
            assert!(
                error.contains("could not persist session rename"),
                "{error}"
            );
            if rollback_fails {
                assert!(error.contains("rollback to old also failed"), "{error}");
                assert!(
                    error.contains("actual tmux identity: renamed session renamed is present"),
                    "{error}"
                );
                assert!(error.contains("manual recovery required"), "{error}");
            } else {
                assert!(error.contains("tmux was restored to old"), "{error}");
                assert_rename_input(&manager, "old", "after-rollback").await;
            }
            assert_eq!(manager.tmux.exists("old").await, !rollback_fails);
            assert_eq!(manager.tmux.exists("renamed").await, rollback_fails);
            assert_eq!(manager.config().await.sessions[0].name, "old");
            assert!(manager.live.read().await.contains_key("old"));
            assert_eq!(std::fs::read(&manager.cfg_path).unwrap(), persisted);
            cleanup_rename_fixture(&manager, root, &socket, &["old", "renamed"]).await;
        }
    }

    #[tokio::test]
    async fn abandoning_update_cannot_skip_commit_or_rollback() {
        let (manager, root, socket) = rename_fixture(true).await;
        std::fs::create_dir_all(manager.cfg_path.with_extension("toml.tmp")).unwrap();
        let (renamed, release) = manager.tmux.pause_after_rename_for_test();
        let update = tokio::spawn({
            let manager = manager.clone();
            async move { manager.update("old", replacement("renamed")).await }
        });
        renamed.notified().await;
        update.abort();
        release.notify_one();

        tokio::time::timeout(std::time::Duration::from_secs(2), async {
            loop {
                if manager.tmux.exists("old").await && !manager.tmux.exists("renamed").await {
                    break;
                }
                tokio::time::sleep(std::time::Duration::from_millis(10)).await;
            }
        })
        .await
        .expect("detached update did not finish rollback");
        assert_eq!(manager.config().await.sessions[0].name, "old");
        cleanup_rename_fixture(&manager, root, &socket, &["old", "renamed"]).await;
    }

    #[tokio::test]
    async fn session_and_project_targets_resolve_from_config_temp_and_host_state() {
        let manager = crate::session::test_manager(Config::default());
        let configured_project = ProjectCfg {
            name: "repo".into(),
            dir: "/tmp/repo".into(),
            ..Default::default()
        };
        let configured = SessionCfg {
            name: "agent".into(),
            project: "repo".into(),
            ..Default::default()
        };
        let cfg = Config {
            projects: vec![configured_project.clone()],
            sessions: vec![configured.clone()],
            ..Default::default()
        };
        *manager.cfg.write().await = cfg.clone();

        assert_eq!(manager.session_cfg("agent").await.unwrap().name, "agent");
        let resolved = manager.project_for(&cfg, &configured).await.unwrap();
        assert_eq!(resolved.name, "repo");
        assert_eq!(resolved.dir, "/tmp/repo");

        let temporary = ProjectCfg {
            name: "scratch".into(),
            dir: "/tmp/scratch".into(),
            temp: true,
            ..Default::default()
        };
        manager
            .temp
            .write()
            .await
            .insert("scratch".into(), temporary.clone());
        let scratch = SessionCfg {
            name: "scratch-agent".into(),
            project: "scratch".into(),
            ..Default::default()
        };
        let resolved = manager.project_for(&cfg, &scratch).await.unwrap();
        assert_eq!(resolved.name, temporary.name);
        assert_eq!(resolved.dir, temporary.dir);

        let mut host = Live::new(
            SessionCfg {
                name: "host-shell".into(),
                project: "gone".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        host.host = true;
        host.host_path = "/tmp/remembered".into();
        manager.live.write().await.insert("host-shell".into(), host);
        let orphan_host = SessionCfg {
            name: "host-shell".into(),
            project: "gone".into(),
            ..Default::default()
        };
        assert_eq!(
            manager.project_for(&cfg, &orphan_host).await.unwrap().dir,
            "/tmp/remembered"
        );
    }

    #[tokio::test]
    async fn state_classification_prefers_rules_then_activity_age() {
        let manager = crate::session::test_manager(Config::default());
        *manager.rules.write().await = vec![(
            State::Waiting,
            regex::Regex::new("choose an option").unwrap(),
        )];
        assert_eq!(
            manager.classify(false, 0, "choose an option").await,
            State::Waiting
        );

        manager.rules.write().await.clear();
        assert_eq!(
            manager.classify(true, u64::MAX, "changed").await,
            State::Working
        );
        assert_eq!(manager.classify(false, 0, "stale").await, State::Idle);
        assert_eq!(
            manager.classify_initial(State::Idle, "unchanged").await,
            State::Idle
        );
        assert_eq!(
            manager.classify_initial(State::Down, "first frame").await,
            State::Working
        );
    }

    #[tokio::test]
    async fn stale_working_rule_matches_decay_to_idle() {
        let manager = crate::session::test_manager(Config::default());
        *manager.rules.write().await = vec![(
            State::Working,
            regex::Regex::new("esc to interrupt").unwrap(),
        )];

        assert_eq!(
            manager
                .classify(false, 0, "* Thinking... (esc to interrupt)")
                .await,
            State::Idle
        );
        assert_eq!(
            manager
                .classify(false, now_ms(), "* Thinking... (esc to interrupt)")
                .await,
            State::Working
        );
    }

    #[tokio::test]
    async fn retick_moves_a_quiet_working_session_to_idle() {
        let manager = crate::session::test_manager(Config::default());
        *manager.rules.write().await = vec![(
            State::Working,
            regex::Regex::new("esc to interrupt").unwrap(),
        )];
        let mut live = Live::new(
            SessionCfg {
                name: "agent".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        live.state = State::Working;
        live.state_since = 0;
        live.last_change = 0;
        live.seq = 1;
        live.screen = Some(ScreenView {
            name: "agent".into(),
            seq: 1,
            cols: 80,
            rows: 24,
            cx: 0,
            cy: 0,
            off: 0,
            history: 0,
            cursor_shape: 0,
            cursor_blink: false,
            app_mouse: false,
            app_drag: false,
            alt_screen: false,
            title: String::new(),
            request_id: 0,
            lines: Vec::new(),
        });
        live.plain = Arc::new("* Thinking... (esc to interrupt)".into());
        manager.live.write().await.insert("agent".into(), live);

        manager.retick().await;

        assert_eq!(manager.live.read().await["agent"].state, State::Idle);
    }

    #[tokio::test]
    async fn views_keep_host_paths_and_sort_by_session_name() {
        let manager = crate::session::test_manager(Config::default());
        *manager.cfg.write().await = Config {
            sessions: vec![SessionCfg {
                name: "agent".into(),
                label: Some("Agent label".into()),
                ..Default::default()
            }],
            ..Default::default()
        };
        let mut agent = Live::new(
            SessionCfg {
                name: "agent".into(),
                label: Some("Agent label".into()),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        agent.state = State::Working;
        agent.seq = 4;
        let mut host = Live::new(
            SessionCfg {
                name: "z-shell".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        host.host = true;
        host.ephemeral = true;
        host.process_running = true;
        host.host_path = "/tmp/host-cwd".into();
        manager.live.write().await.insert("z-shell".into(), host);
        manager.live.write().await.insert("agent".into(), agent);

        let views = manager.views().await;
        assert_eq!(
            views.iter().map(|v| v.name.as_str()).collect::<Vec<_>>(),
            ["agent", "z-shell"]
        );
        assert_eq!(views[0].label, "Agent label");
        assert_eq!(views[0].state, State::Working);
        assert_eq!(views[0].seq, 4);
        assert_eq!(views[1].dir, "/tmp/host-cwd");
        assert!(views[1].host && views[1].ephemeral);
        assert!(views[1].process_running);
    }

    #[test]
    fn host_commands_update_foreground_process_state() {
        let mut live = Live::new(SessionCfg::default(), TitleCapture::default());
        assert!(update_host_process(&mut live, "python"));
        assert!(live.process_running);
        assert!(update_host_process(&mut live, "/bin/bash"));
        assert!(!live.process_running);
        assert!(!update_host_process(&mut live, "/bin/bash"));
    }

    async fn metadata_fixture() -> Arc<Manager> {
        let manager = crate::session::test_manager(Config::default());
        let dir = manager
            .cfg_path
            .parent()
            .unwrap()
            .to_string_lossy()
            .into_owned();
        manager
            .update_cfg(|cfg| {
                cfg.projects.push(ProjectCfg {
                    name: "repo".into(),
                    dir,
                    ..Default::default()
                });
                cfg.sessions.push(SessionCfg {
                    name: "agent".into(),
                    project: "repo".into(),
                    ..Default::default()
                });
                Ok(())
            })
            .await
            .unwrap();
        manager
            .remember_host_terminal("shell", "repo", "/old")
            .await
            .unwrap();
        for (name, host) in [("shell", true), ("agent", false)] {
            let mut live = Live::new(
                SessionCfg {
                    name: name.into(),
                    project: "repo".into(),
                    ..Default::default()
                },
                TitleCapture::default(),
            );
            live.host = host;
            live.host_path = "/old".into();
            live.state = State::Idle;
            live.run_id = 17;
            manager.live.write().await.insert(name.into(), live);
        }
        manager
    }

    #[tokio::test]
    async fn host_metadata_ignores_stale_runs_down_sessions_and_agents() {
        let manager = metadata_fixture().await;
        assert_eq!(
            manager.host_metadata_targets().await,
            vec![("shell".into(), 17)]
        );
        for (name, run, path) in [
            ("shell", 16, "/stale"),
            ("agent", 17, "/agent"),
            ("missing", 17, "/missing"),
            ("shell", 17, "  "),
        ] {
            assert!(!manager.remember_host_path_for_run(name, run, path).await);
        }
        manager.live.write().await.get_mut("shell").unwrap().state = State::Down;
        assert!(manager.host_metadata_targets().await.is_empty());
        assert!(
            !manager
                .remember_host_path_for_run("shell", 17, "/down")
                .await
        );
        assert_eq!(manager.live.read().await["shell"].host_path, "/old");
        assert_eq!(manager.config().await.host_terminals[0].path, "/old");
        std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
    }

    #[tokio::test]
    async fn current_host_metadata_persists_path_and_updates_foreground_process() {
        let manager = metadata_fixture().await;
        let metadata = |path: &str, command: &str| {
            [(
                "shell".into(),
                crate::tmux::HostMetadata {
                    path: Some(path.into()),
                    command: Some(command.into()),
                },
            )]
            .into_iter()
            .collect()
        };
        manager
            .apply_host_metadata(vec![("shell".into(), 16)], metadata("/stale", "python"))
            .await;
        assert!(!manager.live.read().await["shell"].process_running);
        manager
            .apply_host_metadata(
                vec![("shell".into(), 17), ("missing".into(), 17)],
                metadata("/current", "python"),
            )
            .await;
        assert_eq!(manager.live.read().await["shell"].host_path, "/current");
        assert!(manager.live.read().await["shell"].process_running);
        let saved: Config =
            toml::from_str(&std::fs::read_to_string(&manager.cfg_path).unwrap()).unwrap();
        assert_eq!(saved.host_terminals[0].path, "/current");
        assert!(
            !manager
                .remember_host_path_for_run("shell", 17, "/current")
                .await
        );
        manager
            .apply_host_metadata(
                vec![("shell".into(), 17)],
                metadata("/current", "/bin/bash"),
            )
            .await;
        assert!(!manager.live.read().await["shell"].process_running);
        std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
    }

    #[tokio::test]
    async fn remembered_host_edits_preserve_labels_and_reject_agent_collisions() {
        let manager = metadata_fixture().await;
        manager
            .set_label("shell", "  My shell  ".into())
            .await
            .unwrap();
        manager
            .remember_host_terminal("shell", "repo", "/new")
            .await
            .unwrap();
        let cfg = manager.config().await;
        assert_eq!(cfg.host_terminals.len(), 1);
        assert_eq!(cfg.host_terminals[0].label.as_deref(), Some("My shell"));
        assert_eq!(cfg.host_terminals[0].path, "/new");
        assert!(cfg.host_terminals[0].autostart);
        assert!(manager
            .remember_host_terminal("agent", "repo", "/collision")
            .await
            .unwrap_err()
            .to_string()
            .contains("conflicts"));
        assert_eq!(manager.config().await.host_terminals.len(), 1);
        std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
    }

    #[tokio::test]
    async fn manual_labels_count_unicode_characters_clear_and_invalidate_pending_titles() {
        let manager = metadata_fixture().await;
        for name in ["agent", "shell"] {
            let generation = {
                let mut live = manager.live.write().await;
                let row = live.get_mut(name).unwrap();
                row.title.pending = true;
                row.title.override_title = Some("old automatic title".into());
                row.title.generation
            };
            let label = "界".repeat(MAX_MANUAL_LABEL_CHARS);
            manager
                .set_label(name, format!("  {label}  "))
                .await
                .unwrap();
            {
                let live = manager.live.read().await;
                assert_eq!(live[name].cfg.label.as_deref(), Some(label.as_str()));
                assert_eq!(live[name].title.generation, generation.wrapping_add(1));
                assert!(!live[name].title.pending);
                if name == "shell" {
                    assert!(live[name].title.override_title.is_none());
                }
            }
            assert!(manager
                .set_label(name, "界".repeat(MAX_MANUAL_LABEL_CHARS + 1))
                .await
                .is_err());
            assert_eq!(
                manager.live.read().await[name].cfg.label.as_deref(),
                Some(label.as_str())
            );
            manager.set_label(name, "  ".into()).await.unwrap();
            assert!(manager.live.read().await[name].cfg.label.is_none());
        }
        let saved: Config =
            toml::from_str(&std::fs::read_to_string(&manager.cfg_path).unwrap()).unwrap();
        assert!(saved.sessions[0].label.is_none());
        assert!(saved.host_terminals[0].label.is_none());
        assert!(manager
            .set_label("missing", "label".into())
            .await
            .unwrap_err()
            .to_string()
            .contains("no such session"));
        std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
    }

    #[tokio::test]
    async fn ephemeral_labels_stay_runtime_only() {
        let manager = metadata_fixture().await;
        let before = std::fs::read(&manager.cfg_path).unwrap();
        let mut live = Live::new(
            SessionCfg {
                name: "worker".into(),
                project: "repo".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        live.ephemeral = true;
        manager.live.write().await.insert("worker".into(), live);
        manager
            .set_label("worker", "Temporary worker".into())
            .await
            .unwrap();
        assert_eq!(
            manager.live.read().await["worker"].cfg.label.as_deref(),
            Some("Temporary worker")
        );
        assert!(manager.config().await.session("worker").is_none());
        assert_eq!(std::fs::read(&manager.cfg_path).unwrap(), before);
        std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
    }

    #[tokio::test]
    async fn failed_label_persistence_keeps_live_and_config_labels_unchanged() {
        let manager = metadata_fixture().await;
        manager.set_label("agent", "Original".into()).await.unwrap();
        let generation = manager.live.read().await["agent"].title.generation;
        std::fs::remove_file(&manager.cfg_path).unwrap();
        std::fs::create_dir(&manager.cfg_path).unwrap();
        assert!(manager.set_label("agent", "Unsaved".into()).await.is_err());
        assert_eq!(
            manager.live.read().await["agent"].cfg.label.as_deref(),
            Some("Original")
        );
        assert_eq!(
            manager.live.read().await["agent"].title.generation,
            generation
        );
        assert_eq!(
            manager
                .config()
                .await
                .session("agent")
                .unwrap()
                .label
                .as_deref(),
            Some("Original")
        );
        std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
    }
}
