//! Worktree operations do not depend on task outcomes or worker lifetimes.
use super::super::*;
use crate::worktrees::{git, Store, Worktree};
use anyhow::anyhow;
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::os::unix::fs::MetadataExt;
use std::time::SystemTime;

type FileStamp = (SystemTime, u64, u64, u64);

#[derive(Default)]
pub(crate) struct WorktreeViewCache {
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

impl Manager {
    /// Reload the index when the catalog is replaced or edited externally.
    pub(super) async fn worktree_view_index(&self) -> Arc<HashMap<String, Worktree>> {
        let mut cache = self.worktree_views.lock().await;
        let stamp = file_stamp(&self.cfg_path.with_file_name("worktrees.toml")).await;
        if cache.stamp.as_ref() != Some(&stamp) {
            let store = Store::load(&self.cfg_path).await.unwrap_or_default();
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

    pub(super) async fn config_for_worktree_path(
        &self,
        project: &str,
        path: &str,
    ) -> Result<Config> {
        let mut cfg = self.config().await;
        let selected = self.worktree_for_path(project, path).await?;
        if !selected.is_empty() {
            let p = cfg
                .projects
                .iter_mut()
                .find(|p| p.name == project)
                .context("worktree project")?;
            *p = self.resolve_worktree(p, &selected).await?;
        }
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
        let _lock = self.worktree_mutation.lock().await;
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
            if store
                .worktrees
                .iter()
                .any(|w| !p.id.is_empty() && w.project_id == p.id)
            {
                let retained = new.projects.iter().find(|n| n.id == p.id).ok_or_else(|| {
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
            if let Some(previous) = old.session(&session.name) {
                if previous.worktree != session.worktree && self.tmux.exists(&session.name).await {
                    bail!("stop session {} before changing its worktree", session.name);
                }
            }
            if !session.worktree.is_empty() && session.worktree != "main" {
                let p = new
                    .project(&session.project)
                    .ok_or_else(|| anyhow!("unknown worktree project"))?;
                if !store
                    .worktrees
                    .iter()
                    .any(|w| !p.id.is_empty() && w.id == session.worktree && w.project_id == p.id)
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
                bail!("Project mount {} -> {} exposes the original checkout. Edit the mount before selecting a worktree.", mount.from, mount.to);
            }
        }
        let mut resolved = p.clone();
        resolved.dir = w.path.clone();
        resolved.temp = false;
        Ok(resolved)
    }

    async fn worktree_attachments(&self, w: &Worktree) -> Vec<String> {
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
            if !path.is_dir() {
                w.phase = "error".into();
                w.error =
                    "The checkout directory is missing. Retry manual removal to reconcile its record."
                        .into();
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
        manager
            .session_read_operation(async {
                let _lock = manager.worktree_mutation.lock().await;
                let p = manager
                    .update_cfg_if_changed_within_boundary(|cfg| {
                        let p = cfg
                            .projects
                            .iter_mut()
                            .find(|p| p.name == q.project)
                            .ok_or_else(|| anyhow!("no project {}", q.project))?;
                        if p.id.is_empty() {
                            p.id = uuid::Uuid::new_v4().to_string();
                        }
                        uuid::Uuid::parse_str(&p.id).context("invalid project identity")?;
                        Ok((p.clone(), true))
                    })
                    .await?;
                let root = PathBuf::from(expand(&p.dir)).canonicalize()?;
                let repository = git(
                    &root,
                    &["rev-parse", "--path-format=absolute", "--git-common-dir"],
                )
                .await?;
                let id = uuid::Uuid::new_v4().to_string();
                let branch = format!("slopworld/{id}");
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
                let managed = q.path.is_empty();
                let path = if managed {
                    let parent = if p.worktree_root.is_empty() {
                        crate::worktrees::default_root()?
                    } else {
                        PathBuf::from(expand(&p.worktree_root))
                    };
                    if !parent.is_absolute() {
                        bail!("worktree root must be absolute");
                    }
                    parent.join(&p.id).join(&id).join("checkout")
                } else {
                    PathBuf::from(expand(&q.path)).canonicalize()?
                };
                if let Some(why) = crate::sandbox::refused(&path.to_string_lossy()) {
                    bail!("worktree reaches {why}");
                }
                if !managed {
                    let common = git(
                        &path,
                        &["rev-parse", "--path-format=absolute", "--git-common-dir"],
                    )
                    .await?;
                    if Path::new(&common).canonicalize()?
                        != Path::new(&repository).canonicalize()?
                    {
                        bail!("external worktree belongs to another repository");
                    }
                    let top = git(&path, &["rev-parse", "--show-toplevel"]).await?;
                    if Path::new(&top).canonicalize()? != path {
                        bail!("worktree path must be the checkout root");
                    }
                    if path == root {
                        bail!("original checkout is already registered as main");
                    }
                }
                let path = if managed {
                    let container = path.parent().context("worktree container")?;
                    tokio::fs::create_dir_all(
                        container.parent().context("worktree project directory")?,
                    )
                    .await?;
                    tokio::fs::create_dir(container).await?;
                    container.canonicalize()?.join("checkout")
                } else {
                    path
                };
                let mut store = Store::load(&manager.cfg_path).await?;
                if store.worktrees.iter().any(|w| Path::new(&w.path) == path) {
                    bail!("checkout already registered");
                }
                let mut w = Worktree {
                    id,
                    project_id: p.id,
                    name: q.name,
                    path: path.to_string_lossy().into_owned(),
                    repository,
                    managed,
                    initial_branch: if managed {
                        branch.clone()
                    } else {
                        String::new()
                    },
                    base: base.clone(),
                    phase: "allocating".into(),
                    error: String::new(),
                };
                if w.name.is_empty() {
                    w.name = w.id.chars().take(8).collect();
                }
                store.worktrees.push(w.clone());
                store.save(&manager.cfg_path).await?;
                let result = if managed {
                    async {
                        tokio::fs::create_dir_all(path.parent().context("worktree parent")?)
                            .await?;
                        crate::worktrees::allocate(&root, &path, &branch, &base).await?;
                        Ok::<_, anyhow::Error>(())
                    }
                    .await
                } else {
                    Ok(())
                };
                w.phase = if result.is_ok() { "ready" } else { "error" }.into();
                w.error = result.err().map(|e| format!("{e:#}")).unwrap_or_default();
                *store.worktrees.last_mut().unwrap() = w.clone();
                store.save(&manager.cfg_path).await?;
                manager.announce_projects().await;
                if !w.error.is_empty() {
                    bail!(
                        "worktree {} retained after allocation failure: {}",
                        w.id,
                        w.error
                    );
                }
                Ok(w)
            })
            .await
    }

    pub(crate) async fn remove_worktree(
        self: &Arc<Self>,
        project: String,
        id: String,
    ) -> Result<()> {
        let manager = self.clone();
        manager.session_operation(async {
            let _lock = manager.worktree_mutation.lock().await;
            let cfg = manager.config().await;
            let p = cfg.project(&project).ok_or_else(|| anyhow!("no project {project}"))?;
            let mut store = Store::load(&manager.cfg_path).await?;
            let i = store.worktrees.iter().position(|w| w.id == id && w.project_id == p.id && !p.id.is_empty())
                .ok_or_else(|| anyhow!("no removable worktree {id}"))?;
            let w = store.worktrees[i].clone();
            let users = manager.worktree_attachments(&w).await;
            if !users.is_empty() { bail!("Worktree remains attached to {}. Remove or move these sessions first.", users.join(", ")); }
            store.worktrees[i].phase = "removing".into(); store.save(&manager.cfg_path).await?;
            let result = async {
                if !w.managed { return Ok(()); } // Unregister external checkouts. Never delete their files.
                let path = Path::new(&w.path);
                if path.exists() {
                    if path.is_symlink() { bail!("A symlink replaced the worktree path."); }
                    let common = git(path, &["rev-parse", "--path-format=absolute", "--git-common-dir"]).await?;
                    if Path::new(&common).canonicalize()? != Path::new(&w.repository).canonicalize()? { bail!("worktree repository identity changed"); }
                    let status = git(path, &["status", "--porcelain=v1", "--untracked-files=all", "--ignored", "--ignore-submodules=none"]).await?;
                    if !status.is_empty() { bail!("The worktree has tracked changes, untracked files, or ignored files. Resolve them before removal."); }
                    let head = git(path, &["rev-parse", "--verify", "HEAD"]).await?;
                    if git(path, &["for-each-ref", "--format=%(refname)", &format!("--contains={head}"), "refs/heads/"]).await?.is_empty() {
                        bail!("HEAD has no retained local branch. Create one before you remove the worktree.");
                    }
                    crate::worktrees::remove_tree(&w).await?;
                } else {
                    // Git can remove the checkout before the daemon saves the record.
                    // Remove only this Git registration. Do not prune other worktrees.
                    let listing = git(Path::new(&w.repository), &["worktree", "list", "--porcelain", "-z"]).await?;
                    if listing.split('\0').any(|line| line == format!("worktree {}", w.path)) {
                        crate::worktrees::forget_missing(&w).await?;
                    }
                    let container = path.parent().context("worktree container")?;
                    if container.file_name().and_then(|s| s.to_str()) != Some(&w.id) { bail!("worktree container identity changed"); }
                    if container.exists() { std::fs::remove_dir(container).context("worktree container is not empty")?; }
                }
                Ok::<_, anyhow::Error>(())
            }.await;
            match result {
                Ok(()) => { store.worktrees.remove(i); store.save(&manager.cfg_path).await?; Ok(()) }
                Err(e) => { store.worktrees[i].phase = if Path::new(&w.path).is_dir() { "ready" } else { "error" }.into(); store.worktrees[i].error = format!("{e:#}"); store.save(&manager.cfg_path).await?; Err(e) }
            }
        }).await
    }
}

#[cfg(test)]
#[path = "worktrees_tests.rs"]
mod tests;
