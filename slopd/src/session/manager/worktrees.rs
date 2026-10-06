//! Worktree operations do not depend on task outcomes or worker lifetimes.
use super::super::*;
use super::directories::CreatedDirectories;
use crate::session::manager::config::ConfigMutation;
use crate::worktrees::{Store, Worktree, git};
use anyhow::anyhow;
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::os::unix::fs::MetadataExt;
use std::time::SystemTime;

/// Worktree mutation serialization and cached disk records.
#[derive(Default)]
pub(crate) struct WorktreeState {
    pub(super) mutation: tokio::sync::Mutex<()>,
    views: tokio::sync::Mutex<WorktreeViewCache>,
    #[cfg(test)]
    pub(super) relocation_pause:
        std::sync::Mutex<Option<(Arc<tokio::sync::Notify>, Arc<tokio::sync::Notify>)>>,
}

type FileStamp = (SystemTime, u64, u64, u64);

#[derive(Default)]
struct WorktreeViewCache {
    stamp: Option<Option<FileStamp>>,
    by_id: Arc<HashMap<String, Worktree>>,
}

async fn file_stamp(path: &Path) -> Option<FileStamp> {
    let metadata = tokio::fs::metadata(path).await.ok()?;
    Some((
        metadata.modified().ok()?,
        metadata.len(),
        metadata.dev(),
        metadata.ino(),
    ))
}

#[derive(Clone, Default, Deserialize)]
pub(crate) struct WorktreeRequest {
    pub project: String,
    #[serde(default)]
    pub name: String,
    #[serde(default)]
    pub base: String,
    /// An existing external checkout path. An empty path allocates a managed worktree.
    #[serde(default)]
    pub path: String,
}

#[derive(Serialize)]
pub(crate) struct WorktreeView {
    #[serde(flatten)]
    pub worktree: Worktree,
    pub branch: String,
    pub head: String,
    pub attachments: Vec<String>,
}

struct PreparedWorktree {
    root: PathBuf,
    project: ProjectCfg,
    branch: String,
    base: String,
    worktree: Worktree,
}

impl Manager {
    pub(crate) async fn rename_worktree(
        self: &Arc<Self>,
        project: String,
        id: String,
        name: String,
    ) -> Result<Worktree> {
        crate::config::project_name_component(&name).context("invalid worktree name")?;
        let manager = self.clone();
        self.owned_session_operation(Box::pin(async move {
            let _lock = manager.worktrees.mutation.lock().await;
            let cfg = manager.config().await;
            let p = cfg
                .project(&project)
                .ok_or_else(|| anyhow!("no project {project}"))?;
            let store = Store::load(&manager.cfg_path).await?;
            let index = store
                .worktrees
                .iter()
                .position(|w| w.id == id && w.project_id == p.id)
                .ok_or_else(|| anyhow!("no worktree {id}"))?;
            let old = store
                .worktrees
                .get(index)
                .ok_or_else(|| anyhow!("no worktree {id}"))?
                .clone();
            if !old.managed || old.phase != "ready" {
                bail!("only ready managed worktrees can be renamed");
            }
            let users = manager.worktree_attachments(&old).await;
            if !users.is_empty() {
                bail!(
                    "Worktree remains attached to {}. Remove or move these sessions first.",
                    users.join(", ")
                );
            }
            let old_path = Path::new(&old.path);
            if old_path.file_name().and_then(|s| s.to_str()) != Some(&old.name) {
                bail!("worktree path does not match its recorded name");
            }
            let project_dir = old_path.parent().context("worktree root")?.to_path_buf();
            tokio::fs::create_dir_all(&project_dir).await?;
            let destination = checked_worktree_destination(&project_dir, &name).await?;
            let mut updated = old.clone();
            updated.name = name;
            updated.path = destination.to_string_lossy().into_owned();
            if old.path != updated.path {
                if tokio::fs::symlink_metadata(&destination).await.is_ok() {
                    bail!("destination {} already exists", destination.display());
                }
                let mut relocations = crate::worktrees::relocation::Relocations::new(
                    store,
                    vec![(index, updated.clone())],
                )?;
                relocations.execute(&manager.cfg_path).await?;
            }
            Ok(updated)
        }))
        .await
    }
    /// Reload the index when the catalog is replaced or edited externally.
    pub(super) async fn worktree_view_index(&self) -> Arc<HashMap<String, Worktree>> {
        let mut cache = self.worktrees.views.lock().await;
        let stamp = file_stamp(&self.cfg_path.with_file_name("worktrees.toml")).await;
        if cache.stamp.as_ref() != Some(&stamp) {
            let store = match Store::load(&self.cfg_path).await {
                Ok(store) => store,
                Err(error) => {
                    tracing::warn!(
                        "could not reload worktree index; keeping the last valid index: {error:#}"
                    );
                    return cache.by_id.clone();
                }
            };
            cache.by_id = Arc::new(
                store
                    .worktrees
                    .into_iter()
                    .map(|w| (w.id.clone(), w))
                    .collect(),
            );
            cache.stamp = Some(stamp);
        }
        cache.by_id.clone()
    }
    pub(crate) async fn worktree_for_path(&self, project: &str, raw_path: &str) -> Result<String> {
        let cfg = self.config().await;
        let Some(p) = cfg.project(project) else {
            return Ok(String::new());
        };
        let path = absolute_path(raw_path)?;
        let path = path.canonicalize().unwrap_or(path);
        let store = Store::load(&self.cfg_path).await?;
        Ok(store
            .worktrees
            .iter()
            .filter(|w| !p.id.is_empty() && w.project_id == p.id && path.starts_with(&w.path))
            .max_by_key(|w| w.path.len())
            .map(|w| w.id.clone())
            .unwrap_or_default())
    }

    // Empty worktree selects Main. A supplied path must belong to that exact checkout.
    // The action path validator also rejects symlink escapes.
    pub(super) async fn config_for_action_scope(
        &self,
        project: &str,
        worktree: &str,
        path: &str,
    ) -> Result<Config> {
        let worktree = if worktree.is_empty() {
            "main"
        } else {
            worktree
        };
        if project.trim().is_empty() {
            if worktree != "main" {
                bail!("a project is required to select a worktree");
            }
            return Ok(self.config().await);
        }
        if !path.trim().is_empty() {
            let inferred = self.worktree_for_path(project, path).await?;
            let inferred = if inferred.is_empty() {
                "main"
            } else {
                &inferred
            };
            if inferred != worktree {
                bail!("File action path belongs to a different worktree");
            }
        }
        let mut cfg = self.config().await;
        let p = cfg
            .projects
            .iter_mut()
            .find(|p| p.name == project)
            .ok_or_else(|| anyhow!("Project {project:?} does not exist."))?;
        *p = self.resolve_worktree(p, worktree).await?;
        Ok(cfg)
    }

    pub(crate) async fn worktree_base(
        &self,
        caller: &str,
        project: &str,
        revision: &str,
    ) -> Result<String> {
        let cfg = self.config().await;
        let project = cfg
            .project(project)
            .ok_or_else(|| anyhow!("unknown project"))?;
        let selected = if revision.is_empty() && caller != crate::tasks::HOST {
            self.session_cfg(caller)
                .await
                .map(|s| s.worktree)
                .unwrap_or_default()
        } else {
            String::new()
        };
        let project = self.resolve_worktree(project, &selected).await?;
        git(
            Path::new(&expand(&project.dir)),
            &[
                "rev-parse",
                "--verify",
                "--end-of-options",
                &format!(
                    "{}^{{commit}}",
                    if revision.is_empty() {
                        "HEAD"
                    } else {
                        revision
                    }
                ),
            ],
        )
        .await
    }

    pub(crate) async fn recover_worktrees(&self) -> Result<()> {
        let _lock = self.worktrees.mutation.lock().await;
        let mut store = Store::load(&self.cfg_path).await?;
        let mut changed = false;
        for w in &mut store.worktrees {
            if !["allocating", "removing"].contains(&w.phase.as_str()) {
                continue;
            }
            changed = true;
            let complete = if w.phase == "allocating" && Path::new(&w.path).is_dir() {
                let status = git(
                    Path::new(&w.path),
                    &["status", "--porcelain=v1", "--untracked-files=all"],
                )
                .await;
                let head = git(Path::new(&w.path), &["rev-parse", "--verify", "HEAD"]).await;
                matches!((status, head), (Ok(status), Ok(head)) if status.is_empty() && head == w.base)
            } else {
                false
            };
            if complete {
                w.phase = "ready".into();
                w.error.clear();
            } else {
                w.error = format!(
                    "Operation {} was interrupted. Inspect the checkout or try manual removal.",
                    w.phase
                );
                w.phase = "error".into();
            }
        }
        if changed {
            store.save(&self.cfg_path).await?;
        }
        Ok(())
    }

    pub(super) async fn validate_worktree_config(&self, old: &Config, new: &Config) -> Result<()> {
        let store = Store::load(&self.cfg_path).await?;
        // Worker removal validates the whole candidate. Index cross-references
        // once so sessions and worker-owned worktrees do not multiply scans.
        let old_sessions = old.session_index();
        let mut projects_by_name = HashMap::new();
        let mut projects_by_id = HashMap::new();
        for project in &new.projects {
            projects_by_name
                .entry(project.name.as_str())
                .or_insert(project);
            projects_by_id.entry(project.id.as_str()).or_insert(project);
        }
        let worktree_projects: std::collections::HashSet<_> = store
            .worktrees
            .iter()
            .map(|w| w.project_id.as_str())
            .collect();
        let worktrees: std::collections::HashSet<_> = store
            .worktrees
            .iter()
            .map(|w| (w.project_id.as_str(), w.id.as_str()))
            .collect();
        let mut ids = std::collections::HashSet::new();
        for p in &new.projects {
            if !p.id.is_empty() {
                uuid::Uuid::parse_str(&p.id).context("invalid project identity")?;
                if !ids.insert(&p.id) {
                    bail!("duplicate project identity {}", p.id);
                }
            }
        }
        for p in &old.projects {
            if !p.id.is_empty() && worktree_projects.contains(p.id.as_str()) {
                let retained = projects_by_id.get(p.id.as_str()).ok_or_else(|| {
                    anyhow!(
                        "remove project {} worktrees explicitly before removing its identity",
                        p.name
                    )
                })?;
                if retained.dir != p.dir {
                    bail!(
                        "remove project worktrees before changing the original checkout directory"
                    );
                }
            }
        }
        for session in &new.sessions {
            if let Some(previous) = old_sessions.get(session.name.as_str())
                && previous.worktree != session.worktree
                && self.tmux.exists(&session.name).await
            {
                bail!("stop session {} before changing its worktree", session.name);
            }
            if !session.worktree.is_empty() && session.worktree != "main" {
                let p = projects_by_name
                    .get(session.project.as_str())
                    .ok_or_else(|| anyhow!("unknown worktree project"))?;
                if p.id.is_empty()
                    || !worktrees.contains(&(p.id.as_str(), session.worktree.as_str()))
                {
                    bail!("session {} selects an unknown worktree", session.name);
                }
            }
        }
        Ok(())
    }

    pub(crate) async fn resolve_worktree(&self, p: &ProjectCfg, id: &str) -> Result<ProjectCfg> {
        if id.is_empty() || id == "main" {
            return Ok(p.clone());
        }
        let store = Store::load(&self.cfg_path).await?;
        let w = store
            .worktrees
            .iter()
            .find(|w| w.id == id && w.project_id == p.id && !p.id.is_empty())
            .ok_or_else(|| anyhow!("no worktree {id} in project {}", p.name))?;
        if w.phase != "ready" {
            bail!("worktree {id} is not ready (phase {})", w.phase);
        }
        let checkout = Path::new(&w.path);
        if w.managed && checkout.file_name().and_then(|s| s.to_str()) != Some(&w.name) {
            bail!("managed worktree {id} path does not match its recorded name");
        }
        if checkout.is_symlink() || !checkout.is_dir() {
            bail!("worktree {id} checkout is missing or is not a directory");
        }
        let repository = Path::new(&w.repository).canonicalize()?;
        for metadata in crate::worktrees::metadata_paths(checkout)? {
            if !metadata.starts_with(&repository) {
                bail!("worktree Git metadata no longer belongs to its registered repository");
            }
        }
        let original = std::path::PathBuf::from(expand(&p.dir)).canonicalize()?;
        for mount in &p.mounts {
            if mount.mode == crate::config::MountMode::Cache {
                crate::sandbox::cache::validate(p, mount)?;
                let target = std::path::PathBuf::from(crate::config::mount_target(p, &mount.to));
                if std::path::Path::new(&expand(&mount.to)).is_absolute()
                    && target.starts_with(&original)
                {
                    bail!("cache destination must be relative to follow the selected worktree");
                }
                continue;
            }
            let source = std::path::PathBuf::from(expand(&mount.from)).canonicalize()?;
            let target = std::path::PathBuf::from(crate::config::mount_target(p, &mount.to));
            if source.starts_with(&original)
                || original.starts_with(&source)
                || (std::path::Path::new(&mount.to).is_absolute() && target.starts_with(&original))
            {
                bail!(
                    "Project mount {} -> {} exposes the original checkout. Edit the mount before selecting a worktree.",
                    mount.from,
                    mount.to
                );
            }
        }
        let mut resolved = p.clone();
        resolved.dir = w.path.clone();
        resolved.temp = false;
        Ok(resolved)
    }

    pub(super) async fn worktree_attachments(&self, w: &Worktree) -> Vec<String> {
        let cfg = self.config().await;
        let live = self.live.read().await;
        let mut users: Vec<String> = cfg
            .sessions
            .iter()
            .chain(live.values().map(|l| &l.cfg))
            .filter(|s| s.worktree == w.id)
            .map(|s| s.name.clone())
            .collect();
        for host in &cfg.host_terminals {
            if Path::new(&host.path).starts_with(&w.path) {
                users.push(host.name.clone());
            }
        }
        for l in live.values() {
            if l.host && Path::new(&l.host_path).starts_with(&w.path) {
                users.push(l.cfg.name.clone());
            }
        }
        users.sort();
        users.dedup();
        users
    }

    pub(crate) async fn worktree_list(&self, project: &str) -> Result<Vec<WorktreeView>> {
        let cfg = self.config().await;
        let p = cfg
            .project(project)
            .ok_or_else(|| anyhow!("no project {project}"))?;
        let mut rows = vec![Worktree {
            id: "main".into(),
            project_id: p.id.clone(),
            name: "main".into(),
            path: expand(&p.dir),
            phase: "ready".into(),
            ..Default::default()
        }];
        rows.extend(
            Store::load(&self.cfg_path)
                .await?
                .worktrees
                .into_iter()
                .filter(|w| !p.id.is_empty() && w.project_id == p.id),
        );
        let mut result = Vec::new();
        for mut w in rows {
            let path = Path::new(&w.path);
            let head = git(path, &["rev-parse", "--verify", "HEAD"])
                .await
                .unwrap_or_default();
            let branch = git(path, &["symbolic-ref", "--quiet", "--short", "HEAD"])
                .await
                .unwrap_or_default();
            // Keep interrupted requests visible for inspection during this daemon run.
            // Startup recovery identifies completed allocations but never restarts workers.
            if w.phase == "allocating" {
                w.error = "Allocation was interrupted. Inspect the worktree before you remove or reuse it.".into();
            }
            if w.phase == "relocating" {
                // The persisted error includes both source and destination; preserve it.
            } else if !path.is_dir() {
                w.phase = "error".into();
                w.error =
                    "The checkout directory is missing. Retry manual removal to reconcile its record."
                        .into();
            } else if w.managed && path.file_name().and_then(|s| s.to_str()) != Some(&w.name) {
                w.phase = "error".into();
                w.error = "The managed checkout path does not match its recorded name.".into();
            }
            let mut attachments = self.worktree_attachments(&w).await;
            if w.id == "main" {
                attachments = self
                    .views()
                    .await
                    .into_iter()
                    .filter(|s| s.project == project && Path::new(&expand(&s.dir)) == path)
                    .map(|s| s.name)
                    .collect();
            }
            result.push(WorktreeView {
                worktree: w,
                branch,
                head,
                attachments,
            });
        }
        Ok(result)
    }

    pub(crate) async fn create_worktree(self: &Arc<Self>, q: WorktreeRequest) -> Result<Worktree> {
        self.reload_if_changed().await;
        let manager = self.clone();
        Box::pin(self.owned_session_read_operation(async move {
            let _lock = manager.worktrees.mutation.lock().await;
            let prepared = manager.prepare_worktree(&q).await?;
            manager.finish_worktree_creation(prepared).await
        }))
        .await
    }

    async fn prepare_worktree(self: &Arc<Self>, q: &WorktreeRequest) -> Result<PreparedWorktree> {
        let project = self
            .update_cfg_if_changed_inner(ConfigMutation::Projects, |cfg| {
                let project = cfg
                    .projects
                    .iter_mut()
                    .find(|project| project.name == q.project)
                    .ok_or_else(|| anyhow!("no project {}", q.project))?;
                if project.id.is_empty() {
                    project.id = uuid::Uuid::new_v4().to_string();
                }
                uuid::Uuid::parse_str(&project.id).context("invalid project identity")?;
                Ok((project.clone(), true))
            })
            .await?;
        let root = PathBuf::from(expand(&project.dir)).canonicalize()?;
        let repository = git(
            &root,
            &["rev-parse", "--path-format=absolute", "--git-common-dir"],
        )
        .await?;
        let id = uuid::Uuid::new_v4().to_string();
        let name = if q.name.is_empty() {
            id.get(..8).unwrap_or(&id).to_string()
        } else {
            q.name.clone()
        };
        crate::config::project_name_component(&name).context("invalid worktree name")?;
        let managed = q.path.is_empty();
        if managed {
            crate::worktrees::validate_new_branch(&root, &name).await?;
        }
        let branch = name.clone();
        let base = git(
            &root,
            &[
                "rev-parse",
                "--verify",
                "--end-of-options",
                &format!(
                    "{}^{{commit}}",
                    if q.base.is_empty() { "HEAD" } else { &q.base }
                ),
            ],
        )
        .await?;
        let requested_path = if managed {
            let parent = crate::worktrees::project_root(&project);
            if !parent.is_absolute() {
                bail!("worktree root must be absolute");
            }
            parent.join(&name)
        } else {
            PathBuf::from(expand(&q.path)).canonicalize()?
        };
        if let Some(why) = crate::sandbox::refused(&requested_path.to_string_lossy()) {
            bail!("worktree reaches {why}");
        }
        if !managed {
            validate_external_worktree(&root, &repository, &requested_path).await?;
        }
        let path = requested_path;
        Ok(PreparedWorktree {
            root,
            project: project.clone(),
            branch: branch.clone(),
            base: base.clone(),
            worktree: Worktree {
                id,
                project_id: project.id,
                name,
                path: path.to_string_lossy().into_owned(),
                repository,
                managed,
                initial_branch: if managed { branch } else { String::new() },
                base,
                phase: "allocating".into(),
                error: String::new(),
            },
        })
    }

    async fn finish_worktree_creation(&self, prepared: PreparedWorktree) -> Result<Worktree> {
        let PreparedWorktree {
            root,
            project,
            branch,
            base,
            mut worktree,
        } = prepared;
        let mut directories = CreatedDirectories::default();
        let mut store = Store::load(&self.cfg_path).await?;
        if store
            .worktrees
            .iter()
            .any(|registered| registered.path == worktree.path)
        {
            bail!("checkout already registered");
        }
        if worktree.managed {
            let path = create_managed_worktree_path(
                PathBuf::from(&worktree.path),
                &worktree.name,
                &mut directories,
            )
            .await?;
            worktree.path = path.to_string_lossy().into_owned();
            if store
                .worktrees
                .iter()
                .any(|registered| registered.path == worktree.path)
            {
                bail!("checkout already registered");
            }
        }
        let path = Path::new(&worktree.path);
        store.worktrees.push(worktree.clone());
        store.save(&self.cfg_path).await?;
        // The allocating record now owns crash recovery for this directory.
        directories.commit();

        let result = if worktree.managed {
            crate::worktrees::allocate(&root, path, &branch, &base).await
        } else {
            Ok(())
        }
        .and_then(|()| crate::sandbox::cache::reconcile(&project, path));
        worktree.phase = if result.is_ok() { "ready" } else { "error" }.into();
        worktree.error = result
            .err()
            .map(|error| format!("{error:#}"))
            .unwrap_or_default();
        *store
            .worktrees
            .last_mut()
            .ok_or_else(|| anyhow!("created worktree disappeared before publication"))? =
            worktree.clone();
        store.save(&self.cfg_path).await?;
        self.announce_projects().await;
        if !worktree.error.is_empty() {
            bail!(
                "worktree {} retained after allocation failure: {}",
                worktree.id,
                worktree.error
            );
        }
        Ok(worktree)
    }

    pub(crate) async fn remove_worktree(
        self: &Arc<Self>,
        project: String,
        id: String,
    ) -> Result<()> {
        self.session_operation(self.remove_worktree_inner(&project, &id))
            .await
    }

    async fn remove_worktree_inner(&self, project: &str, id: &str) -> Result<()> {
        // Keep project identity and catalog writes serialized through deletion or recovery.
        let _mutation = self.worktrees.mutation.lock().await;
        let mut removal = self.prepare_worktree_removal(project, id).await?;
        removal.record_intent(&self.cfg_path).await?;
        let result = remove_worktree_checkout(&removal.project, &removal.worktree).await;
        removal.finish(&self.cfg_path, result).await
    }

    async fn prepare_worktree_removal(&self, project: &str, id: &str) -> Result<WorktreeRemoval> {
        let cfg = self.config().await;
        let project = cfg
            .project(project)
            .ok_or_else(|| anyhow!("no project {project}"))?
            .clone();
        let store = Store::load(&self.cfg_path).await?;
        let index = store
            .worktrees
            .iter()
            .position(|w| w.id == id && w.project_id == project.id && !project.id.is_empty())
            .ok_or_else(|| anyhow!("no removable worktree {id}"))?;
        let worktree = store
            .worktrees
            .get(index)
            .ok_or_else(|| anyhow!("no removable worktree {id}"))?
            .clone();
        if worktree.phase == "relocating" {
            bail!(
                "Repair the interrupted relocation before removing this worktree: {}",
                worktree.error
            );
        }
        let users = self.worktree_attachments(&worktree).await;
        if !users.is_empty() {
            bail!(
                "Worktree remains attached to {}. Remove or move these sessions first.",
                users.join(", ")
            );
        }
        Ok(WorktreeRemoval {
            project,
            worktree,
            store,
            index,
        })
    }
}

/// The catalog mutation guard keeps this selected record and its index stable.
/// Retain the original record while the catalog stores the removal intent.
struct WorktreeRemoval {
    project: ProjectCfg,
    worktree: Worktree,
    store: Store,
    index: usize,
}

impl WorktreeRemoval {
    fn record_mut(&mut self) -> Result<&mut Worktree> {
        self.store
            .worktrees
            .get_mut(self.index)
            .ok_or_else(|| anyhow!("no removable worktree {}", self.worktree.id))
    }

    async fn record_intent(&mut self, config: &Path) -> Result<()> {
        self.record_mut()?.phase = "removing".into();
        self.store.save(config).await
    }

    async fn finish(mut self, config: &Path, result: Result<()>) -> Result<()> {
        match result {
            Ok(()) => {
                self.store.worktrees.remove(self.index);
                self.store.save(config).await
            }
            Err(error) => {
                // A failed deletion remains inspectable and retryable. A failed catalog
                // save after deletion instead leaves the durable removing intent for recovery.
                let phase = if Path::new(&self.worktree.path).is_dir() {
                    "ready"
                } else {
                    "error"
                };
                let record = self.record_mut()?;
                record.phase = phase.into();
                record.error = format!("{error:#}");
                self.store.save(config).await?;
                Err(error)
            }
        }
    }
}

async fn remove_worktree_checkout(project: &ProjectCfg, worktree: &Worktree) -> Result<()> {
    // Unregister external checkouts without touching their files or Git registration.
    if !worktree.managed {
        return Ok(());
    }
    let path = Path::new(&worktree.path);
    if !path.exists() {
        return forget_missing_worktree(worktree).await;
    }
    validate_removal_repository(worktree).await?;

    // Cache contents outlive checkouts. Remove only their unchanged links before
    // cleanliness inspection, and restore those links if validation or deletion fails.
    let links = crate::sandbox::cache::remove_links(project, path)?;
    let result = remove_clean_worktree(worktree).await;
    if result.is_err() && path.is_dir() {
        crate::sandbox::cache::restore_links(&links)?;
    }
    result
}

async fn validate_removal_repository(worktree: &Worktree) -> Result<()> {
    let path = Path::new(&worktree.path);
    if path.is_symlink() {
        bail!("A symlink replaced the worktree path.");
    }
    let common = git(
        path,
        &["rev-parse", "--path-format=absolute", "--git-common-dir"],
    )
    .await?;
    if Path::new(&common).canonicalize()? != Path::new(&worktree.repository).canonicalize()? {
        bail!("worktree repository identity changed");
    }
    Ok(())
}

async fn remove_clean_worktree(worktree: &Worktree) -> Result<()> {
    let path = Path::new(&worktree.path);
    let status = git(
        path,
        &[
            "status",
            "--porcelain=v1",
            "--untracked-files=all",
            "--ignored",
            "--ignore-submodules=none",
        ],
    )
    .await?;
    anyhow::ensure!(
        status.is_empty(),
        "The worktree has tracked changes, untracked files, or ignored files. Resolve them before removal."
    );
    let head = git(path, &["rev-parse", "--verify", "HEAD"]).await?;
    let retained_branches = git(
        path,
        &[
            "for-each-ref",
            "--format=%(refname)",
            &format!("--contains={head}"),
            "refs/heads/",
        ],
    )
    .await?;
    anyhow::ensure!(
        !retained_branches.is_empty(),
        "HEAD has no retained local branch. Create one before you remove the worktree."
    );
    crate::worktrees::remove_tree(worktree).await
}

async fn forget_missing_worktree(worktree: &Worktree) -> Result<()> {
    // Git can delete the checkout before its record is saved. Retire only this
    // registration; never prune unrelated worktrees or delete a recreated checkout.
    let listing = git(
        Path::new(&worktree.repository),
        &["worktree", "list", "--porcelain", "-z"],
    )
    .await?;
    let registration = format!("worktree {}", worktree.path);
    if listing.split('\0').any(|line| line == registration) {
        crate::worktrees::forget_missing(worktree).await?;
    }
    Ok(())
}

async fn validate_external_worktree(root: &Path, repository: &str, path: &Path) -> Result<()> {
    let common = git(
        path,
        &["rev-parse", "--path-format=absolute", "--git-common-dir"],
    )
    .await?;
    if Path::new(&common).canonicalize()? != Path::new(repository).canonicalize()? {
        bail!("external worktree belongs to another repository");
    }
    let top = git(path, &["rev-parse", "--show-toplevel"]).await?;
    if Path::new(&top).canonicalize()? != path {
        bail!("worktree path must be the checkout root");
    }
    if path == root {
        bail!("original checkout is already registered as main");
    }
    Ok(())
}

/// Callers own directory creation and collision policy; all managed destinations share these guards.
pub(super) async fn checked_worktree_destination(
    project_dir: &Path,
    name: &str,
) -> Result<PathBuf> {
    if tokio::fs::symlink_metadata(project_dir)
        .await?
        .file_type()
        .is_symlink()
    {
        bail!("worktree project directory cannot be a symlink");
    }
    let path = project_dir.canonicalize()?.join(name);
    if let Some(why) = crate::sandbox::refused(&path.to_string_lossy()) {
        bail!("worktree reaches {why}");
    }
    Ok(path)
}

async fn create_managed_worktree_path(
    requested_path: PathBuf,
    name: &str,
    directories: &mut CreatedDirectories,
) -> Result<PathBuf> {
    let project_dir = requested_path
        .parent()
        .context("worktree project directory")?;
    directories.create_all(project_dir)?;
    let path = checked_worktree_destination(project_dir, name).await?;
    match std::fs::symlink_metadata(&path) {
        Ok(_) => bail!("worktree path {} already exists", path.display()),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
        Err(error) => return Err(error.into()),
    }
    directories.create_all(&path)?;
    Ok(path)
}

#[cfg(test)]
#[path = "worktrees_tests.rs"]
mod tests;
