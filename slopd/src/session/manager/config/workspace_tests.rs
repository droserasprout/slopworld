//! Real manager/Git integration against the fixture-selected record layout.
use super::*;
use crate::config::HostTerminalCfg;
use crate::session::WorktreeRequest;
use crate::storage::{target::StorageBinding, workspace};
use std::time::Duration;

struct Fixture {
    manager: Arc<Manager>,
    binding: StorageBinding,
    root: PathBuf,
}
impl Fixture {
    async fn new() -> Self {
        let manager = crate::session::test_manager(Config::default());
        let root = manager.cfg_path.parent().unwrap().to_owned();
        let repo = root.join("repo");
        std::fs::create_dir(&repo).unwrap();
        for args in [
            vec!["init", "-q"],
            vec![
                "-c",
                "user.name=Test",
                "-c",
                "user.email=test@example.invalid",
                "commit",
                "--allow-empty",
                "-qm",
                "initial",
            ],
        ] {
            assert!(
                std::process::Command::new("git")
                    .arg("-C")
                    .arg(&repo)
                    .args(args)
                    .status()
                    .unwrap()
                    .success()
            );
        }
        manager.cfg.write().await.projects.push(ProjectCfg {
            id: "1111111111111111".into(),
            name: "repo".into(),
            dir: repo.to_string_lossy().into_owned(),
            worktree_root: root.join("trees").to_string_lossy().into_owned(),
            ..Default::default()
        });
        let binding = super::record_tests::select_records(&manager).await;
        std::fs::write(&manager.cfg_path, "# root stays unchanged\n").unwrap();
        Self {
            manager,
            binding,
            root,
        }
    }
    async fn tree(&self, name: &str) -> crate::worktrees::Worktree {
        self.manager
            .create_worktree(WorktreeRequest {
                project: "repo".into(),
                name: name.into(),
                ..Default::default()
            })
            .await
            .unwrap()
    }
    fn tree_path(&self, id: &str) -> PathBuf {
        self.binding
            .data
            .join("worktrees")
            .join(format!("{id}.toml"))
    }
    fn project_path(&self) -> PathBuf {
        self.binding.config.join("projects/1111111111111111.toml")
    }
}
impl Drop for Fixture {
    fn drop(&mut self) {
        drop(std::fs::remove_dir_all(&self.root));
    }
}

#[tokio::test]
async fn worktree_records_keep_main_derived_siblings_unchanged_and_ignore_external_edits() {
    let f = Fixture::new().await;
    let first = f.tree("first").await;
    let second = f.tree("second").await;
    assert!(crate::storage_id::valid(&first.id));
    let sibling = std::fs::read(f.tree_path(&second.id)).unwrap();
    let sibling_stamp = std::fs::metadata(f.tree_path(&second.id))
        .unwrap()
        .modified()
        .unwrap();
    let project = std::fs::read(f.project_path()).unwrap();
    let renamed = f
        .manager
        .rename_worktree("repo".into(), first.id.clone(), "renamed".into())
        .await
        .unwrap();
    assert_eq!(renamed.id, first.id);
    assert!(!Path::new(&first.path).exists());
    assert!(Path::new(&renamed.path).is_dir());
    assert_eq!(std::fs::read(f.tree_path(&second.id)).unwrap(), sibling);
    assert_eq!(
        std::fs::metadata(f.tree_path(&second.id))
            .unwrap()
            .modified()
            .unwrap(),
        sibling_stamp
    );
    assert_eq!(std::fs::read(f.project_path()).unwrap(), project);
    let rows = f.manager.worktree_list("repo").await.unwrap();
    assert_eq!(
        rows.iter()
            .map(|row| row.worktree.id.as_str())
            .collect::<Vec<_>>(),
        ["main", &first.id, &second.id]
    );
    assert!(!f.tree_path("main").exists());
    assert!(!f.manager.cfg_path.with_file_name("worktrees.toml").exists());
    std::fs::write(f.tree_path(&first.id), "broken external edit").unwrap();
    std::fs::write(
        f.manager.cfg_path.with_file_name("worktrees.toml"),
        "broken legacy catalog",
    )
    .unwrap();
    assert_eq!(
        f.manager.worktree_view_index().await[&first.id].path,
        renamed.path
    );
    assert_eq!(
        f.manager.load_worktrees().await.unwrap().worktrees[0].path,
        renamed.path
    );
    assert_eq!(
        std::fs::read_to_string(&f.manager.cfg_path).unwrap(),
        "# root stays unchanged\n"
    );
}

#[tokio::test]
async fn project_rename_commits_agent_and_host_references_together_and_preserves_snapshots() {
    let f = Fixture::new().await;
    // Seed dependent records through their actual manager mutation owners.
    f.manager
        .update_cfg(ConfigMutation::Agents, |cfg| {
            cfg.sessions.push(SessionCfg {
                name: "agent".into(),
                project: "repo".into(),
                state_id: "2222222222222222".into(),
                cmd: Some("true".into()),
                command_snapshot: Some(crate::presets::CommandPreset {
                    name: "captured".into(),
                    cmd: "original".into(),
                    ..Default::default()
                }),
                ..Default::default()
            });
            Ok(())
        })
        .await
        .unwrap();
    f.manager
        .update_cfg(ConfigMutation::HostShells, |cfg| {
            cfg.host_terminals.push(HostTerminalCfg {
                id: "3333333333333333".into(),
                name: "host".into(),
                project: "repo".into(),
                path: f.root.join("repo").to_string_lossy().into_owned(),
                ..Default::default()
            });
            Ok(())
        })
        .await
        .unwrap();
    let agent = f.binding.data.join("agents/2222222222222222.toml");
    let host = f.binding.data.join("host_shells/3333333333333333.toml");
    let before_agent = std::fs::read(&agent).unwrap();
    let before_host = std::fs::read(&host).unwrap();
    let before_project = std::fs::read(f.project_path()).unwrap();
    let mut next = f.manager.config().await.projects[0].clone();
    next.name = "renamed".into();
    let fault = crate::paths::fail_writes(&host);
    f.manager
        .update_project("repo", next.clone())
        .await
        .unwrap_err();
    assert_eq!(std::fs::read(&agent).unwrap(), before_agent);
    assert_eq!(std::fs::read(&host).unwrap(), before_host);
    assert_eq!(std::fs::read(f.project_path()).unwrap(), before_project);
    assert_eq!(f.manager.config().await.sessions[0].project, "repo");
    drop(fault);
    f.manager.update_project("repo", next).await.unwrap();
    let cfg = f.manager.config().await;
    assert_eq!(cfg.sessions[0].project, "renamed");
    assert_eq!(
        cfg.sessions[0].command_snapshot.as_ref().unwrap().cmd,
        "original"
    );
    assert_eq!(cfg.host_terminals[0].project, "renamed");
    assert_eq!(cfg.projects[0].id, "1111111111111111");
    assert!(f.manager.remove_project("renamed").await.is_err());
    assert_eq!(
        std::fs::read_to_string(&f.manager.cfg_path).unwrap(),
        "# root stays unchanged\n"
    );
}

#[tokio::test]
async fn canceled_project_move_rolls_back_git_and_records_after_final_write_failure() {
    let f = Fixture::new().await;
    let tree = f.tree("checkout").await;
    let old_project = std::fs::read(f.project_path()).unwrap();
    let reached = Arc::new(tokio::sync::Notify::new());
    let release = Arc::new(tokio::sync::Notify::new());
    *f.manager.worktrees.relocation_pause.lock().unwrap() =
        Some((reached.clone(), release.clone()));
    let mut next = f.manager.config().await.projects[0].clone();
    next.name = "renamed".into();
    let request = tokio::spawn({
        let manager = f.manager.clone();
        async move { manager.update_project("repo", next).await }
    });
    tokio::time::timeout(Duration::from_secs(10), reached.notified())
        .await
        .unwrap();
    let pending = workspace::Store::<crate::worktrees::Worktree>::load(&f.binding)
        .await
        .unwrap()
        .ordered();
    assert_eq!(pending[0].phase, "relocating");
    assert!(pending[0].error.contains(&tree.path));
    assert!(!Path::new(&tree.path).exists());
    assert!(f.root.join("trees/renamed/checkout").exists());
    let fault = crate::paths::fail_writes(&f.project_path());
    request.abort();
    release.notify_one();
    let _boundary =
        tokio::time::timeout(Duration::from_secs(10), f.manager.session_boundary.write())
            .await
            .unwrap();
    assert!(Path::new(&tree.path).exists());
    assert!(!f.root.join("trees/renamed/checkout").exists());
    assert_eq!(std::fs::read(f.project_path()).unwrap(), old_project);
    let stored = workspace::Store::<crate::worktrees::Worktree>::load(&f.binding)
        .await
        .unwrap()
        .ordered();
    assert_eq!(stored[0].phase, "ready");
    assert_eq!(stored[0].path, tree.path);
    assert_eq!(
        f.manager.load_worktrees().await.unwrap().worktrees[0].path,
        tree.path
    );
    assert!(!f.binding.journal().unwrap().exists());
    drop(fault);
}

#[tokio::test]
async fn failed_git_rollback_retains_relocation_intent_instead_of_ready_stale_paths() {
    let f = Fixture::new().await;
    let tree = f.tree("checkout").await;
    let reached = Arc::new(tokio::sync::Notify::new());
    let release = Arc::new(tokio::sync::Notify::new());
    *f.manager.worktrees.relocation_pause.lock().unwrap() =
        Some((reached.clone(), release.clone()));
    let mut next = f.manager.config().await.projects[0].clone();
    next.name = "renamed".into();
    let request = tokio::spawn({
        let manager = f.manager.clone();
        async move { manager.update_project("repo", next).await }
    });
    tokio::time::timeout(Duration::from_secs(10), reached.notified())
        .await
        .unwrap();
    // Occupying the original path makes reverse movement refuse to overwrite it.
    std::fs::create_dir(&tree.path).unwrap();
    let fault = crate::paths::fail_writes(&f.project_path());
    release.notify_one();
    let error = request.await.unwrap().unwrap_err();
    assert!(format!("{error:#}").contains("restoring project worktrees also failed"));
    drop(fault);
    let stored = workspace::Store::<crate::worktrees::Worktree>::load(&f.binding)
        .await
        .unwrap()
        .ordered();
    assert_eq!(stored[0].phase, "relocating");
    assert!(stored[0].error.contains("renamed"));
    assert!(f.root.join("trees/renamed/checkout").exists());
    f.manager.recover_worktrees().await.unwrap();
    assert_eq!(
        f.manager.load_worktrees().await.unwrap().worktrees[0].phase,
        "relocating"
    );
    assert!(
        f.manager
            .remove_worktree("repo".into(), tree.id)
            .await
            .is_err()
    );
}

#[tokio::test]
async fn project_move_commits_ready_paths_only_with_the_new_project() {
    let f = Fixture::new().await;
    let tree = f.tree("checkout").await;
    let mut next = f.manager.config().await.projects[0].clone();
    next.name = "renamed".into();
    f.manager.update_project("repo", next).await.unwrap();
    let stored = workspace::Store::<crate::worktrees::Worktree>::load(&f.binding)
        .await
        .unwrap()
        .ordered();
    assert_eq!(stored[0].phase, "ready");
    assert_eq!(stored[0].id, tree.id);
    assert_eq!(
        stored[0].path,
        f.root.join("trees/renamed/checkout").to_string_lossy()
    );
    assert_eq!(
        workspace::Store::<ProjectCfg>::load(&f.binding)
            .await
            .unwrap()
            .ordered()[0]
            .name,
        "renamed"
    );
    assert!(!f.binding.journal().unwrap().exists());
}

#[tokio::test]
async fn removing_external_record_leaves_checkout_and_siblings_untouched() {
    let f = Fixture::new().await;
    let tree = f.tree("external").await;
    let sibling = f.tree("sibling").await;
    let sibling_bytes = std::fs::read(f.tree_path(&sibling.id)).unwrap();
    f.manager
        .session_operation(async {
            let _worktrees = f.manager.worktrees.mutation.lock().await;
            let mut store = f.manager.load_worktrees().await.unwrap();
            store.worktrees[0].managed = false;
            f.manager.save_worktrees(&store).await.unwrap();
        })
        .await;
    f.manager
        .remove_worktree("repo".into(), tree.id.clone())
        .await
        .unwrap();
    assert!(Path::new(&tree.path).exists());
    assert!(!f.tree_path(&tree.id).exists());
    assert_eq!(
        std::fs::read(f.tree_path(&sibling.id)).unwrap(),
        sibling_bytes
    );
    assert!(
        crate::worktrees::git(&f.root.join("repo"), &["worktree", "list", "--porcelain"])
            .await
            .unwrap()
            .contains(&tree.path)
    );
}

#[tokio::test]
async fn project_creation_uses_short_ids_and_preserves_siblings_through_retirement() {
    let f = Fixture::new().await;
    let before = std::fs::read(f.project_path()).unwrap();
    f.manager
        .add_project(ProjectCfg {
            name: "extra".into(),
            dir: f.root.join("repo").to_string_lossy().into_owned(),
            ..Default::default()
        })
        .await
        .unwrap();
    let cfg = f.manager.config().await;
    let id = cfg.project("extra").unwrap().id.clone();
    assert!(crate::storage_id::valid(&id));
    let path = f.binding.config.join("projects").join(format!("{id}.toml"));
    assert!(path.is_file());
    assert_eq!(cfg.projects[0].name, "repo");
    f.manager.remove_project("extra").await.unwrap();
    assert!(!path.exists());
    assert_eq!(std::fs::read(f.project_path()).unwrap(), before);
}

#[tokio::test]
async fn record_removal_refuses_dirty_or_attached_checkouts_and_retains_files() {
    let f = Fixture::new().await;
    let tree = f.tree("dirty").await;
    std::fs::write(Path::new(&tree.path).join("untracked"), "keep").unwrap();
    let error = f
        .manager
        .remove_worktree("repo".into(), tree.id.clone())
        .await
        .unwrap_err();
    assert!(format!("{error:#}").contains("untracked"));
    assert!(f.tree_path(&tree.id).exists());
    assert!(Path::new(&tree.path).join("untracked").exists());
    assert_eq!(
        f.manager.load_worktrees().await.unwrap().worktrees[0].phase,
        "ready"
    );
    f.manager
        .update_cfg(ConfigMutation::HostShells, |cfg| {
            cfg.host_terminals.push(HostTerminalCfg {
                id: "3333333333333333".into(),
                name: "attached".into(),
                project: "repo".into(),
                path: tree.path.clone(),
                ..Default::default()
            });
            Ok(())
        })
        .await
        .unwrap();
    let before = std::fs::read(f.tree_path(&tree.id)).unwrap();
    let error = f
        .manager
        .remove_worktree("repo".into(), tree.id.clone())
        .await
        .unwrap_err();
    assert!(format!("{error:#}").contains("attached"));
    assert_eq!(std::fs::read(f.tree_path(&tree.id)).unwrap(), before);
    assert!(f.manager.remove_project("repo").await.is_err());
}
