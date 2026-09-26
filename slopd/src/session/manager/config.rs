//! Serialized configuration edits, disk reloads, and runtime publication.

use super::super::*;

// Configuration transaction state.
enum ConfigOrigin {
    StructuredMutation,
    FileUpdate,
    DiskReload { mtime: Option<SystemTime> },
}

/// Validated candidate and its compiled activity rules.
struct ConfigChange {
    new: Config,
    rules: Vec<(State, Regex)>,
    root_token_changed: bool,
}

pub(super) struct PreparedConfigChange {
    change: ConfigChange,
    // Prevent stale candidates, including across a tmux rename before commit.
    _persist: tokio::sync::OwnedMutexGuard<()>,
}

/// Work to perform after publishing the candidate.
struct ConfigEffects {
    endpoint_token: String,
    reconcile: bool,
    announce_sessions: bool,
    announce_projects_and_library: bool,
}

fn prepare_candidate(old: &Config, mut new: Config) -> Result<ConfigChange> {
    if new.daemon.token == crate::config::TOKEN_REDACTED {
        new.daemon.token = old.daemon.token.clone();
    }
    let root_token_changed = old.daemon.token != new.daemon.token;
    validate_config(&new)?;
    let rules = compile_rules(&new);
    Ok(ConfigChange {
        new,
        rules,
        root_token_changed,
    })
}

/// Update project and worktree links; return removed links for rollback if saving fails.
async fn reconcile_cache_links(
    config_path: &Path,
    old: &Config,
    cfg: &Config,
) -> Result<Vec<(PathBuf, PathBuf)>> {
    let store = crate::worktrees::Store::load(config_path).await?;
    let mut removed = Vec::new();
    let result = (|| -> Result<()> {
        for project in &old.projects {
            let next = cfg
                .projects
                .iter()
                .find(|p| p.id == project.id && !p.id.is_empty());
            let obsolete = project
                .mounts
                .iter()
                .filter(|mount| {
                    mount.mode == crate::config::MountMode::Cache
                        && crate::sandbox::cache::relative(mount)
                        && !next.is_some_and(|p| {
                            p.dir == project.dir
                                && p.mounts.iter().any(|m| {
                                    m.mode == crate::config::MountMode::Cache
                                        && crate::sandbox::cache::relative(m)
                                        && expand(&m.to) == expand(&mount.to)
                                })
                        })
                })
                .cloned()
                .collect::<Vec<_>>();
            if obsolete.is_empty() {
                continue;
            }
            let mut retired = project.clone();
            retired.mounts = obsolete;
            let main = PathBuf::from(expand(&project.dir));
            if main.is_dir() {
                removed.extend(crate::sandbox::cache::remove_links(&retired, &main)?);
            }
            for worktree in store
                .worktrees
                .iter()
                .filter(|w| w.project_id == project.id && Path::new(&w.path).is_dir())
            {
                removed.extend(crate::sandbox::cache::remove_links(
                    &retired,
                    Path::new(&worktree.path),
                )?);
            }
        }
        for project in &cfg.projects {
            if project.mounts.iter().all(|m| {
                m.mode != crate::config::MountMode::Cache || !crate::sandbox::cache::relative(m)
            }) {
                continue;
            }
            let main = PathBuf::from(expand(&project.dir));
            crate::sandbox::cache::reconcile(project, &main)?;
            for worktree in store
                .worktrees
                .iter()
                .filter(|w| w.project_id == project.id && Path::new(&w.path).is_dir())
            {
                crate::sandbox::cache::reconcile(project, Path::new(&worktree.path))?;
            }
        }
        Ok(())
    })();
    if let Err(error) = result {
        crate::sandbox::cache::restore_links(&removed)?;
        return Err(error);
    }
    Ok(removed)
}

fn restore_redacted_document_token(old: &Config, document: &mut toml::Value) {
    if let Some(daemon) = document
        .get_mut("daemon")
        .and_then(toml::Value::as_table_mut)
    {
        daemon.insert(
            "token".into(),
            toml::Value::String(old.daemon.token.clone()),
        );
    }
}

impl ConfigOrigin {
    fn effects(&self, endpoint_token: String) -> ConfigEffects {
        ConfigEffects {
            endpoint_token,
            reconcile: !matches!(self, Self::StructuredMutation),
            announce_sessions: matches!(self, Self::DiskReload { .. }),
            announce_projects_and_library: !matches!(self, Self::StructuredMutation),
        }
    }
}

impl Manager {
    // Shared commit steps: persist, publish, then reconcile and notify.

    async fn persist_cfg(&self, cfg: &Config) -> Result<()> {
        cfg.save(&self.cfg_path).await?;
        self.mark_cfg_mtime().await;
        Ok(())
    }

    async fn persist_cfg_text(&self, text: &str) -> Result<()> {
        Config::save_text(&self.cfg_path, text).await?;
        self.mark_cfg_mtime().await;
        Ok(())
    }

    async fn mark_cfg_mtime(&self) {
        *self.config_state.cfg_mtime.lock().unwrap() = disk_mtime(&self.cfg_path).await;
        *self.config_state.library_mtime.lock().unwrap() =
            Config::library_stamp_for(&self.cfg_path);
    }

    async fn publish_config(&self, change: ConfigChange, origin: ConfigOrigin) -> ConfigEffects {
        let endpoint_token = change.new.daemon.token.clone();

        let old = self.config().await;
        for session in &old.sessions {
            if !change
                .new
                .session(&session.name)
                .is_some_and(|next| next.state_id == session.state_id)
            {
                self.invalidate_session(&session.name).await;
            }
        }

        let mut rules = self.rules.compiled.write().await;
        *rules = change.rules;
        self.rules
            .revision
            .fetch_add(1, std::sync::atomic::Ordering::AcqRel);
        drop(rules);
        *self.cfg.write().await = change.new;
        if let ConfigOrigin::DiskReload { mtime } = origin {
            // Do not consume a disk stamp until the accepted contents are visible in memory.
            *self.config_state.cfg_mtime.lock().unwrap() = mtime;
        }
        if change.root_token_changed {
            self.invalidate_auth(crate::session::AuthChange::RootTokenChanged);
        }
        self.signals.maintenance_wake.notify_waiters();

        origin.effects(endpoint_token)
    }

    async fn update_endpoint(&self, token: &str) {
        if let Err(e) = crate::endpoint::update_token(&self.endpoint_path, token).await {
            tracing::warn!("The daemon accepted the config but could not update the endpoint descriptor: {e:#}");
        }
    }

    async fn finish_config_change(self: &Arc<Self>, effects: ConfigEffects) {
        if effects.reconcile {
            self.sync_from_config().await;
        }
        if effects.announce_sessions {
            self.announce_sessions().await;
        }
        if effects.announce_projects_and_library {
            self.announce_projects().await;
            self.announce_library().await;
        }
    }

    // Structured mutations. Callers own session reconciliation and announcements.

    /// Serialize snapshot edits, saving before publication without holding the cfg lock.
    pub(super) async fn update_cfg<T>(
        &self,
        update: impl FnOnce(&mut Config) -> Result<T>,
    ) -> Result<T> {
        self.update_cfg_if_changed(|cfg| update(cfg).map(|result| (result, true)))
            .await
    }

    pub(super) async fn update_cfg_if_changed<T>(
        &self,
        update: impl FnOnce(&mut Config) -> Result<(T, bool)>,
    ) -> Result<T> {
        self.session_operation(self.update_cfg_if_changed_within_boundary(update))
            .await
    }

    pub(super) async fn update_cfg_if_changed_within_boundary<T>(
        &self,
        update: impl FnOnce(&mut Config) -> Result<(T, bool)>,
    ) -> Result<T> {
        let (result, change) = self.prepare_cfg_change(update).await?;
        let Some(change) = change else {
            return Ok(result);
        };
        self.commit_prepared_cfg(change).await?;
        Ok(result)
    }

    /// Validate a private snapshot and retain the persistence lock until commit or drop.
    pub(super) async fn prepare_cfg_change<T>(
        &self,
        update: impl FnOnce(&mut Config) -> Result<(T, bool)>,
    ) -> Result<(T, Option<PreparedConfigChange>)> {
        let persist = self.config_state.persist.clone().lock_owned().await;
        let old = self.cfg.read().await.clone();
        let mut candidate = old.clone();
        let (result, changed) = update(&mut candidate)?;
        if !changed {
            return Ok((result, None));
        }

        self.validate_worktree_config(&old, &candidate).await?;
        let change = prepare_candidate(&old, candidate)?;
        Ok((
            result,
            Some(PreparedConfigChange {
                change,
                _persist: persist,
            }),
        ))
    }

    pub(super) async fn commit_prepared_cfg(&self, prepared: PreparedConfigChange) -> Result<()> {
        let PreparedConfigChange { change, _persist } = prepared;
        let old = self.cfg.read().await.clone();
        let removed = reconcile_cache_links(&self.cfg_path, &old, &change.new).await?;
        if let Err(error) = self.persist_cfg(&change.new).await {
            crate::sandbox::cache::restore_links(&removed)?;
            return Err(error);
        }
        let effects = self
            .publish_config(change, ConfigOrigin::StructuredMutation)
            .await;
        // Keep endpoint token writes ordered with configuration commits.
        self.update_endpoint(&effects.endpoint_token).await;
        drop(_persist);
        debug_assert!(!effects.reconcile);
        Ok(())
    }

    // Disk reloads.

    pub async fn reload_if_changed(self: &Arc<Self>) -> bool {
        if self.session_request_active() {
            return false;
        }
        self.session_operation(self.reload_if_changed_within_boundary())
            .await
    }

    pub(super) async fn reload_if_stale(self: &Arc<Self>) {
        let disk = disk_mtime(&self.cfg_path).await;
        let library = Config::library_stamp_for(&self.cfg_path);
        let stale = *self.config_state.cfg_mtime.lock().unwrap() != disk
            || *self.config_state.library_mtime.lock().unwrap() != library;
        if stale {
            self.reload_if_changed().await;
        }
    }

    async fn reload_if_changed_within_boundary(self: &Arc<Self>) -> bool {
        // Serialize disk reads and publication with saves; release before reconciliation.
        let persist = self.config_state.persist.lock().await;
        let disk = disk_mtime(&self.cfg_path).await;
        let library_disk = Config::library_stamp_for(&self.cfg_path);
        {
            let cfg_seen = self.config_state.cfg_mtime.lock().unwrap();
            let library_seen = self.config_state.library_mtime.lock().unwrap();
            if *cfg_seen == disk && *library_seen == library_disk {
                return false;
            }
        }

        let parsed = match Config::load(&self.cfg_path).await {
            Ok(cfg) => cfg,
            Err(e) => {
                tracing::warn!("config changed on disk but is invalid, keeping the old one: {e:#}");
                return false;
            }
        };
        let old = self.cfg.read().await.clone();
        if let Err(error) = self.validate_worktree_config(&old, &parsed).await {
            tracing::warn!("config worktree validation failed: {error:#}");
            return false;
        }
        let change = match prepare_candidate(&old, parsed) {
            Ok(change) => change,
            Err(e) => {
                tracing::warn!("config changed on disk but is invalid, keeping the old one: {e:#}");
                return false;
            }
        };
        if let Err(error) = reconcile_cache_links(&self.cfg_path, &old, &change.new).await {
            tracing::warn!("config cache links could not be reconciled: {error:#}");
            return false;
        }
        tracing::info!("config changed on disk, reloading");
        let effects = self
            .publish_config(change, ConfigOrigin::DiskReload { mtime: disk })
            .await;
        *self.config_state.library_mtime.lock().unwrap() = library_disk;
        self.update_endpoint(&effects.endpoint_token).await;
        drop(persist);
        self.finish_config_change(effects).await;
        true
    }

    pub async fn config(&self) -> Config {
        self.cfg.read().await.clone()
    }

    // Document edits preserve unmodeled fields and keep the library in its own store.

    /// Merge a JSON object into the current TOML document, then reconcile sessions.
    pub async fn patch_config(self: &Arc<Self>, patch: Value) -> Result<()> {
        self.session_operation(self.patch_config_within_boundary(patch))
            .await
    }

    async fn patch_config_within_boundary(self: &Arc<Self>, patch: Value) -> Result<()> {
        self.reload_if_changed().await;

        let persist = self.config_state.persist.lock().await;
        let text = tokio::fs::read_to_string(&self.cfg_path)
            .await
            .with_context(|| format!("reading {}", self.cfg_path.display()))?;
        let mut document: toml::Value = toml::from_str(&text).context("parsing config.toml")?;
        let patch = json_to_toml(patch)?;
        if !patch.is_table() {
            bail!("config patch must be a JSON object");
        }
        merge_toml(&mut document, patch);

        let old = self.cfg.read().await.clone();
        let (mut parsed, mut document) =
            Config::parse_document(&toml::to_string_pretty(&document)?)?;
        parsed.library = old.library.clone();
        if let Some(table) = document.as_table_mut() {
            table.remove("library");
        }
        if parsed.daemon.token == crate::config::TOKEN_REDACTED {
            restore_redacted_document_token(&old, &mut document);
        }
        self.validate_worktree_config(&old, &parsed).await?;
        let change = prepare_candidate(&old, parsed)?;
        let removed = reconcile_cache_links(&self.cfg_path, &old, &change.new).await?;

        let text = toml::to_string_pretty(&document)?;
        if let Err(error) = self.persist_cfg_text(&text).await {
            crate::sandbox::cache::restore_links(&removed)?;
            return Err(error);
        }
        let effects = self.publish_config(change, ConfigOrigin::FileUpdate).await;
        self.update_endpoint(&effects.endpoint_token).await;
        drop(persist);
        self.finish_config_change(effects).await;
        Ok(())
    }

    /// Validate and replace the TOML document, then reconcile sessions.
    pub async fn replace_config(self: &Arc<Self>, text: &str) -> Result<()> {
        self.session_operation(self.replace_config_within_boundary(text))
            .await
    }

    async fn replace_config_within_boundary(self: &Arc<Self>, text: &str) -> Result<()> {
        let persist = self.config_state.persist.lock().await;
        let old = self.cfg.read().await.clone();
        let (mut parsed, mut document) = Config::parse_document(text)?;
        parsed.library = old.library.clone();
        if let Some(table) = document.as_table_mut() {
            table.remove("library");
        }
        if parsed.daemon.token == crate::config::TOKEN_REDACTED {
            restore_redacted_document_token(&old, &mut document);
        }
        self.validate_worktree_config(&old, &parsed).await?;
        let change = prepare_candidate(&old, parsed)?;
        let removed = reconcile_cache_links(&self.cfg_path, &old, &change.new).await?;
        if let Err(error) = self
            .persist_cfg_text(&toml::to_string_pretty(&document)?)
            .await
        {
            crate::sandbox::cache::restore_links(&removed)?;
            return Err(error);
        }
        let effects = self.publish_config(change, ConfigOrigin::FileUpdate).await;
        self.update_endpoint(&effects.endpoint_token).await;
        drop(persist);
        self.finish_config_change(effects).await;
        Ok(())
    }
}

#[cfg(test)]
#[path = "config_tests.rs"]
mod tests;
