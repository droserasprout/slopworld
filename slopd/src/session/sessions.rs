//! Live edit facts, label application, reader metadata and rename handoffs.
//! Application session_edits owns configuration transactions and private-state coordination.
//! Host metadata belongs to host_metadata; lifecycle owns launch/stop and views owns projection.

use super::lifecycle::stop::{finish_reader, take_reader_for_abort};
use super::*;
use anyhow::anyhow;

impl SessionRuntime {
    pub(crate) async fn live_name_exists(&self, name: &str) -> bool {
        self.live.read().await.contains_key(name)
    }

    /// Capture the original reader label before any persistence or live mutation.
    pub(crate) async fn label_fallback(&self, name: &str) -> Option<String> {
        self.live
            .read()
            .await
            .get(name)
            .filter(|row| !row.launch.intent.is_empty() && !row.launch.original_label.is_empty())
            .map(|row| row.launch.original_label.clone())
    }

    /// Called under the session operation guard after label persistence succeeds.
    pub(crate) async fn apply_label(&self, name: &str, saved: Option<String>) -> Result<()> {
        {
            let mut live = self.live.write().await;
            let Some(live) = live.get_mut(name) else {
                bail!("no such session: {name}");
            };
            live.cfg.label = saved;
            live.title.label_changed(live.role.is_host());
        }
        self.persist_reader_metadata(name).await?;
        self.announce_sessions().await;
        Ok(())
    }

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

    pub(crate) async fn session_cfg(&self, name: &str) -> Option<SessionCfg> {
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

    pub(crate) async fn session_role(&self, name: &str) -> Option<SessionRole> {
        self.live.read().await.get(name).map(|live| live.role)
    }

    pub(crate) async fn is_host(&self, name: &str) -> bool {
        self.live
            .read()
            .await
            .get(name)
            .map(|l| l.role.is_host())
            .unwrap_or(false)
    }

    pub(crate) async fn recover_failed_tmux_rename(
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

    pub(crate) async fn readopt(self: &Arc<Self>, old: &str, new: &str) {
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
}

#[cfg(test)]
#[path = "sessions_tests.rs"]
mod tests;
