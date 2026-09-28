use super::*;

fn repo() -> PathBuf {
    let path = std::env::temp_dir().join(format!("slop-worktree-test-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&path).unwrap();
    let status = std::process::Command::new("git")
        .args(["init", "-q"])
        .arg(&path)
        .status()
        .unwrap();
    assert!(status.success());
    std::fs::write(path.join("file"), "base\n").unwrap();
    for args in [
        vec!["add", "."],
        vec![
            "-c",
            "user.name=Test",
            "-c",
            "user.email=test@example.invalid",
            "commit",
            "-qm",
            "initial",
        ],
    ] {
        assert!(std::process::Command::new("git")
            .arg("-C")
            .arg(&path)
            .args(args)
            .status()
            .unwrap()
            .success());
    }
    path
}

#[tokio::test]
async fn restricted_worktree_creation() {
    let root = repo();
    let id = uuid::Uuid::new_v4().to_string();
    let path = root.join(&id);
    std::fs::create_dir(&path).unwrap();
    let base = git(&root, &["rev-parse", "HEAD"]).await.unwrap();
    let result = allocate(&root, &path, "worker", &base).await;
    assert!(result.is_ok(), "{result:?}");
    assert_eq!(
        std::fs::read_to_string(path.join("file")).unwrap(),
        "base\n"
    );
    let w = Worktree {
        id,
        name: path.file_name().unwrap().to_string_lossy().into_owned(),
        path: path.to_string_lossy().into_owned(),
        repository: root.join(".git").to_string_lossy().into_owned(),
        ..Default::default()
    };
    if mismatched_proc_namespace() {
        // The sandbox command is unavailable in this test namespace.
        // Use ordinary Git to verify worktree contents and registration. Later tests check removal requirements.
        assert_eq!(git(&root, &["rev-parse", "worker"]).await.unwrap(), base);
    } else {
        remove_tree(&w).await.unwrap();
    }
    std::fs::remove_dir_all(root).unwrap();
}

#[tokio::test]
async fn lifecycle_is_independent_and_removal_preserves_branches() {
    use crate::config::{Config, ProjectCfg, SessionCfg};
    use crate::session::{test_manager, WorktreeRequest};
    let root = repo();
    let mut cfg = Config::default();
    cfg.projects.push(ProjectCfg {
        name: "repo".into(),
        dir: root.to_string_lossy().into_owned(),
        worktree_root: root
            .join("trees with spaces")
            .to_string_lossy()
            .into_owned(),
        ..Default::default()
    });
    let manager = test_manager(cfg);
    std::fs::write(root.join("file"), "caller dirty\n").unwrap();
    let w = manager
        .create_worktree(WorktreeRequest {
            project: "repo".into(),
            name: "parallel".into(),
            ..Default::default()
        })
        .await
        .unwrap();
    assert_eq!(Path::new(&w.path).file_name().unwrap(), "parallel");
    assert_eq!(
        Path::new(&w.path).parent().unwrap().file_name().unwrap(),
        "repo"
    );
    assert!(manager
        .rename_worktree("repo".into(), w.id.clone(), "../escape".into())
        .await
        .is_err());
    let occupied = Path::new(&w.path).parent().unwrap().join("occupied");
    std::fs::create_dir(&occupied).unwrap();
    assert!(manager
        .rename_worktree("repo".into(), w.id.clone(), "occupied".into())
        .await
        .is_err());
    assert!(Path::new(&w.path).exists());
    let renamed = manager
        .rename_worktree("repo".into(), w.id.clone(), "feature name".into())
        .await
        .unwrap();
    assert_eq!(
        Path::new(&renamed.path).file_name().unwrap(),
        "feature name"
    );
    assert!(!Path::new(&w.path).exists());
    assert_eq!(
        git(Path::new(&renamed.path), &["rev-parse", "--show-toplevel"])
            .await
            .unwrap(),
        renamed.path
    );
    let w = renamed;
    assert_eq!(
        std::fs::read_to_string(Path::new(&w.path).join("file")).unwrap(),
        "base\n"
    );
    assert_eq!(
        std::fs::read_to_string(root.join("file")).unwrap(),
        "caller dirty\n"
    );
    assert!(manager.tasks.all_tasks().is_empty());
    assert!(manager.config().await.sessions.is_empty());
    assert!(manager.remove_project("repo").await.is_err());
    // Stopped sessions also count as attachments. Removing either attachment preserves the checkout.
    for name in ["one", "two"] {
        manager
            .add(SessionCfg {
                name: name.into(),
                project: "repo".into(),
                worktree: w.id.clone(),
                cmd: Some("true".into()),
                ..Default::default()
            })
            .await
            .unwrap();
    }
    assert_eq!(
        manager.worktree_list("repo").await.unwrap()[1]
            .attachments
            .len(),
        2
    );
    assert!(manager
        .rename_worktree("repo".into(), w.id.clone(), "blocked".into())
        .await
        .is_err());
    assert!(manager
        .remove_worktree("repo".into(), w.id.clone())
        .await
        .is_err());
    manager.remove("one").await.unwrap();
    assert!(Path::new(&w.path).exists());
    manager.remove("two").await.unwrap();
    assert!(Path::new(&w.path).exists());
    // Failed removal neither commits nor destroys files. Retry uses the actual current branch.
    std::fs::write(Path::new(&w.path).join("untracked"), "keep me").unwrap();
    assert!(manager
        .remove_worktree("repo".into(), w.id.clone())
        .await
        .is_err());
    assert_eq!(
        git(Path::new(&w.path), &["rev-parse", "HEAD"])
            .await
            .unwrap(),
        w.base
    );
    std::fs::remove_file(Path::new(&w.path).join("untracked")).unwrap();
    git(
        Path::new(&w.path),
        &["symbolic-ref", "HEAD", "refs/heads/switched"],
    )
    .await
    .unwrap();
    git(
        Path::new(&w.path),
        &["update-ref", "refs/heads/switched", &w.base],
    )
    .await
    .unwrap();
    let rows = manager.worktree_list("repo").await.unwrap();
    assert_eq!(rows[1].branch, "switched");
    assert_eq!(rows[1].worktree.id, w.id);
    if mismatched_proc_namespace() {
        assert!(Path::new(&w.path).exists());
    } else {
        manager.remove_worktree("repo".into(), w.id).await.unwrap();
        assert!(!Path::new(&w.path).exists());
    }
    assert_eq!(
        git(&root, &["rev-parse", &w.initial_branch]).await.unwrap(),
        w.base
    );
    assert_eq!(
        git(&root, &["rev-parse", "switched"]).await.unwrap(),
        w.base
    );
    std::fs::remove_dir_all(root).unwrap();
}

#[tokio::test]
async fn ignored_data_detached_head_and_crash_records_are_preserved() {
    use crate::config::{Config, ProjectCfg};
    use crate::session::{test_manager, WorktreeRequest};
    let root = repo();
    let mut cfg = Config::default();
    cfg.projects.push(ProjectCfg {
        name: "repo".into(),
        dir: root.to_string_lossy().into_owned(),
        worktree_root: root.join("trees").to_string_lossy().into_owned(),
        ..Default::default()
    });
    let manager = test_manager(cfg);
    let w = manager
        .create_worktree(WorktreeRequest {
            project: "repo".into(),
            ..Default::default()
        })
        .await
        .unwrap();
    let path = Path::new(&w.path);
    // Ignored data is not assumed to be disposable build output.
    std::fs::write(root.join(".git/info/exclude"), "precious\n").unwrap();
    std::fs::write(path.join("precious"), "not committed").unwrap();
    assert!(manager
        .remove_worktree("repo".into(), w.id.clone())
        .await
        .unwrap_err()
        .to_string()
        .contains("ignored"));
    assert!(path.join("precious").exists());
    std::fs::remove_file(path.join("precious")).unwrap();
    // Agent-controlled detached HEAD is valid, but new unreachable work blocks teardown.
    std::fs::write(path.join(".git"), std::fs::read(path.join(".git")).unwrap()).unwrap();
    let metadata = metadata_paths(path).unwrap();
    std::fs::write(metadata[0].join("HEAD"), format!("{}\n", w.base)).unwrap();
    std::fs::write(path.join("file"), "detached change\n").unwrap();
    assert!(std::process::Command::new("git")
        .arg("-C")
        .arg(path)
        .args([
            "-c",
            "user.name=Test",
            "-c",
            "user.email=test@example.invalid",
            "commit",
            "-qam",
            "detached work"
        ])
        .status()
        .unwrap()
        .success());
    assert!(manager
        .remove_worktree("repo".into(), w.id.clone())
        .await
        .unwrap_err()
        .to_string()
        .contains("retained local branch"));
    assert_eq!(manager.worktree_list("repo").await.unwrap()[1].branch, "");
    // Restart recovery reports an interrupted operation without committing, deleting or relaunching.
    let mut store = Store::load(&manager.cfg_path).await.unwrap();
    store.worktrees[0].phase = "removing".into();
    store.save(&manager.cfg_path).await.unwrap();
    manager.recover_worktrees().await.unwrap();
    let recovered = Store::load(&manager.cfg_path).await.unwrap();
    assert_eq!(recovered.worktrees[0].phase, "error");
    assert!(path.join("file").exists());
    assert!(manager.config().await.sessions.is_empty());
    std::fs::remove_dir_all(root).unwrap();
}

#[tokio::test]
async fn managed_worktree_branches_match_names_and_validate_before_allocation() {
    use crate::config::{Config, ProjectCfg};
    use crate::session::{test_manager, WorktreeRequest};
    let root = repo();
    let manager = test_manager(Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: root.to_string_lossy().into_owned(),
            ..Default::default()
        }],
        ..Default::default()
    });

    for name in ["invalid name", "bad..name", "HEAD", "-option", "@{-1}"] {
        let invalid = manager
            .create_worktree(WorktreeRequest {
                project: "repo".into(),
                name: name.into(),
                ..Default::default()
            })
            .await
            .unwrap_err();
        assert!(invalid.to_string().contains("invalid Git branch name"));
        assert!(!root.join(".worktrees").exists());
    }

    assert!(std::process::Command::new("git")
        .arg("-C")
        .arg(&root)
        .args(["branch", "already-exists"])
        .status()
        .unwrap()
        .success());
    let collision = manager
        .create_worktree(WorktreeRequest {
            project: "repo".into(),
            name: "already-exists".into(),
            ..Default::default()
        })
        .await
        .unwrap_err();
    assert!(collision.to_string().contains("already exists"));
    assert!(!root.join(".worktrees").exists());
    assert!(Store::load(&manager.cfg_path)
        .await
        .unwrap()
        .worktrees
        .is_empty());

    let named = manager
        .create_worktree(WorktreeRequest {
            project: "repo".into(),
            name: "feature-branch".into(),
            ..Default::default()
        })
        .await
        .unwrap();
    assert_eq!(named.initial_branch, named.name);
    assert_eq!(
        Path::new(&named.path),
        root.join(".worktrees/feature-branch")
    );
    assert_eq!(
        git(Path::new(&named.path), &["symbolic-ref", "--short", "HEAD"])
            .await
            .unwrap(),
        "feature-branch"
    );

    let generated = manager
        .create_worktree(WorktreeRequest {
            project: "repo".into(),
            ..Default::default()
        })
        .await
        .unwrap();
    assert_eq!(generated.initial_branch, generated.name);
    assert_eq!(
        Path::new(&generated.path),
        root.join(".worktrees").join(&generated.name)
    );
    assert_eq!(
        git(
            Path::new(&generated.path),
            &["symbolic-ref", "--short", "HEAD"]
        )
        .await
        .unwrap(),
        generated.name
    );

    // A display-name change must not relocate project-local checkouts.
    let mut project = manager.config().await.projects[0].clone();
    project.name = "renamed".into();
    manager.update_project("repo", project).await.unwrap();
    assert_eq!(
        manager.worktree_list("renamed").await.unwrap()[1]
            .worktree
            .path,
        named.path
    );
    let renamed = manager
        .rename_worktree("renamed".into(), named.id, "new-name".into())
        .await
        .unwrap();
    assert_eq!(Path::new(&renamed.path), root.join(".worktrees/new-name"));
    assert!(!Path::new(&named.path).exists());
    assert_eq!(
        git(
            Path::new(&renamed.path),
            &["symbolic-ref", "--short", "HEAD"]
        )
        .await
        .unwrap(),
        "feature-branch"
    );
    if !mismatched_proc_namespace() {
        manager
            .remove_worktree("renamed".into(), renamed.id)
            .await
            .unwrap();
        assert!(!Path::new(&renamed.path).exists());
        assert!(root.join("file").exists());
        assert!(Path::new(&generated.path).join("file").exists());
    }

    std::fs::remove_dir_all(root).unwrap();
}

// The development agent can run in a PID namespace with the host /proc mounted over it.
// Bubblewrap cannot create nested namespaces in that environment.
// Check this specific failure without treating unrelated removal errors as sandbox unavailability.
fn mismatched_proc_namespace() -> bool {
    std::fs::read_link("/proc/self")
        .is_ok_and(|p| p.to_string_lossy() != std::process::id().to_string())
}

#[tokio::test]
async fn external_checkouts_and_interrupted_teardown_have_independent_records() {
    use crate::config::{Config, ProjectCfg};
    use crate::session::{test_manager, WorktreeRequest};
    let root = repo();
    let manager = test_manager(Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: root.to_string_lossy().into_owned(),
            worktree_root: root.join("trees").to_string_lossy().into_owned(),
            ..Default::default()
        }],
        ..Default::default()
    });
    let w = manager
        .create_worktree(WorktreeRequest {
            project: "repo".into(),
            ..Default::default()
        })
        .await
        .unwrap();
    let mut store = Store::load(&manager.cfg_path).await.unwrap();
    store.worktrees[0].phase = "allocating".into();
    store.save(&manager.cfg_path).await.unwrap();
    manager.recover_worktrees().await.unwrap();
    assert_eq!(
        Store::load(&manager.cfg_path).await.unwrap().worktrees[0].phase,
        "ready"
    );
    let mut project = manager.config().await.projects[0].clone();
    project.name = "renamed".into();
    manager.update_project("repo", project).await.unwrap();
    let renamed_path = manager.worktree_list("renamed").await.unwrap()[1]
        .worktree
        .path
        .clone();
    assert_eq!(
        Path::new(&renamed_path)
            .parent()
            .unwrap()
            .file_name()
            .unwrap(),
        "renamed"
    );
    assert!(Path::new(&renamed_path).exists());
    assert_eq!(
        manager
            .worktree_for_path("renamed", &format!("{}/file", renamed_path))
            .await
            .unwrap(),
        w.id
    );
    // Simulate completed Git removal before the daemon saves the result.
    // Recovery does not delete files. A retry can finish without an existing checkout or nested sandbox.
    assert!(std::process::Command::new("git")
        .arg("-C")
        .arg(&root)
        .args(["worktree", "remove", &renamed_path])
        .status()
        .unwrap()
        .success());
    manager
        .remove_worktree("renamed".into(), w.id)
        .await
        .unwrap();
    assert_eq!(manager.worktree_list("renamed").await.unwrap().len(), 1);
    let external = root.join("external checkout");
    std::fs::create_dir(&external).unwrap();
    let base = git(&root, &["rev-parse", "HEAD"]).await.unwrap();
    allocate(&root, &external, "external", &base).await.unwrap();
    let w = manager
        .create_worktree(WorktreeRequest {
            project: "renamed".into(),
            path: external.to_string_lossy().into_owned(),
            ..Default::default()
        })
        .await
        .unwrap();
    assert!(!w.managed);
    assert!(w.initial_branch.is_empty());
    assert_eq!(
        git(&external, &["symbolic-ref", "--short", "HEAD"])
            .await
            .unwrap(),
        "external"
    );
    manager
        .remove_worktree("renamed".into(), w.id)
        .await
        .unwrap();
    assert!(external.join("file").exists());
    assert!(manager
        .remove_worktree("renamed".into(), "main".into())
        .await
        .is_err());
    std::fs::remove_dir_all(root).unwrap();
}

#[tokio::test]
async fn git_mutations_cannot_follow_metadata_symlinks_outside_the_worktree_grant() {
    let root = repo();
    let outside = root.join("outside");
    std::fs::create_dir(&outside).unwrap();
    std::os::unix::fs::symlink(&outside, root.join(".git/escape")).unwrap();
    let path = root.join(".git/escape/config");
    let result = git_command(
        &root,
        &[
            "config",
            "--file",
            path.to_str().unwrap(),
            "test.value",
            "unsafe",
        ],
        &[&root.join(".git")],
    )
    .await;
    assert!(result.is_err());
    assert!(!outside.join("config").exists());
    let allowed = root.join(".git/allowed-config");
    git_command(
        &root,
        &[
            "config",
            "--file",
            allowed.to_str().unwrap(),
            "test.value",
            "safe",
        ],
        &[&root.join(".git")],
    )
    .await
    .unwrap();
    assert!(std::fs::read_to_string(allowed).unwrap().contains("safe"));
    std::fs::remove_dir_all(root).unwrap();
}

#[tokio::test]
async fn worktree_resolution_mounts_metadata_and_keeps_project_scope() {
    use crate::config::{Config, Mount, MountMode, NetworkMode, ProjectCfg, SessionCfg};
    use crate::session::{test_manager, WorktreeRequest};
    let root = repo();
    let cache = root.with_extension("cache");
    let manager = test_manager(Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: root.to_string_lossy().into_owned(),
            worktree_root: root.join("trees").to_string_lossy().into_owned(),
            mounts: vec![Mount {
                from: cache.to_string_lossy().into_owned(),
                to: "target".into(),
                mode: MountMode::Cache,
            }],
            ..Default::default()
        }],
        ..Default::default()
    });
    let w = manager
        .create_worktree(WorktreeRequest {
            project: "repo".into(),
            ..Default::default()
        })
        .await
        .unwrap();
    let cfg = manager.config().await;
    let project = &cfg.projects[0];
    let effective = manager.resolve_worktree(project, &w.id).await.unwrap();
    assert_eq!(effective.dir, w.path);
    assert_eq!(project.dir, root.to_string_lossy());
    let session = SessionCfg {
        name: "worker".into(),
        project: "repo".into(),
        worktree: w.id.clone(),
        network: NetworkMode::Host,
        cmd: Some("/usr/bin/true".into()),
        ..Default::default()
    };
    let args = crate::sandbox::build_plan(&cfg, &session, &effective)
        .unwrap()
        .lower();
    assert!(args
        .windows(3)
        .any(|v| v[0] == "--bind" && v[1] == w.repository && v[2] == w.repository));
    assert!(args.windows(3).any(|v| v[0] == "--bind"
        && v[1] == cache.to_string_lossy()
        && v[2] == cache.to_string_lossy()));
    assert_eq!(
        std::fs::read_link(Path::new(&w.path).join("target")).unwrap(),
        cache
    );
    assert!(!args.iter().any(|v| v == root.to_str().unwrap()));
    let mut other = project.clone();
    other.id = uuid::Uuid::new_v4().to_string();
    assert!(manager.resolve_worktree(&other, &w.id).await.is_err());
    let mut unsafe_mount = project.clone();
    unsafe_mount.mounts.push(Mount {
        from: root.to_string_lossy().into_owned(),
        to: root.to_string_lossy().into_owned(),
        mode: MountMode::Ro,
    });
    assert!(manager
        .resolve_worktree(&unsafe_mount, &w.id)
        .await
        .is_err());
    assert!(cache.exists());
    std::fs::write(cache.join("artifact"), "kept").unwrap();
    match manager.remove_worktree("repo".into(), w.id).await {
        Ok(()) => assert!(!Path::new(&w.path).exists()),
        Err(error) => {
            // Some CI hosts cannot create Bubblewrap user namespaces. Git removal must
            // restore the link before leaving the checkout available for inspection.
            assert!(error.to_string().contains("Git worktree removal"));
            assert_eq!(
                std::fs::read_link(Path::new(&w.path).join("target")).unwrap(),
                cache
            );
        }
    }
    assert_eq!(
        std::fs::read_to_string(cache.join("artifact")).unwrap(),
        "kept"
    );
    std::fs::remove_dir_all(root).unwrap();
    std::fs::remove_dir_all(cache).unwrap();
}

#[tokio::test]
async fn store_reads_only_worktree_catalog_and_fields() {
    let root = std::env::temp_dir().join(format!("slopd-worktree-store-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&root).unwrap();
    let config = root.join("config.toml");
    let record = Worktree {
        id: "retained".into(),
        path: "/tmp/retained".into(),
        ..Default::default()
    };
    let legacy = toml::to_string(&Store {
        worktrees: vec![record],
    })
    .unwrap()
    .replace("[[worktrees]]", "[[workspaces]]");
    std::fs::write(root.join("workspaces.toml"), legacy).unwrap();
    assert!(Store::load(&config).await.unwrap().worktrees.is_empty());
    std::fs::write(root.join("worktrees.toml"), "[[workspaces]]\nid = 'old'\n").unwrap();
    assert!(Store::load(&config).await.is_err());
    Store::default().save(&config).await.unwrap();
    assert!(Store::load(&config).await.unwrap().worktrees.is_empty());
    assert!(root.join("workspaces.toml").exists());
    std::fs::remove_dir_all(root).unwrap();
}

#[tokio::test]
async fn relocation_rollback_restores_checkout_registration_and_catalog() {
    let root = repo();
    let source = root.join("source");
    std::fs::create_dir(&source).unwrap();
    allocate(&root, &source, "source", "HEAD").await.unwrap();
    let original = Worktree {
        id: "tree".into(),
        name: "source".into(),
        path: source.to_string_lossy().into_owned(),
        repository: root.join(".git").to_string_lossy().into_owned(),
        phase: "ready".into(),
        managed: true,
        ..Default::default()
    };
    let config = root.join("config.toml");
    let mut store = Store::default();
    store.worktrees.push(original.clone());
    store.save(&config).await.unwrap();
    let destination = root.join("destination");
    let mut moved = original.clone();
    moved.name = "destination".into();
    moved.path = destination.to_string_lossy().into_owned();
    let mut relocation = relocation::Relocations::new(store, vec![(0, moved.clone())]);
    relocation.execute(&config).await.unwrap();
    assert_eq!(
        Store::load(&config).await.unwrap().worktrees[0].path,
        moved.path
    );
    relocation.rollback(&config).await.unwrap();
    assert!(!destination.exists());
    assert_eq!(
        Store::load(&config).await.unwrap().worktrees[0].path,
        original.path
    );
    assert_eq!(
        git(&source, &["rev-parse", "--show-toplevel"])
            .await
            .unwrap(),
        original.path
    );
    // Repeated cleanup remains safe after all moves have been restored.
    relocation.rollback(&config).await.unwrap();
    std::fs::remove_dir_all(root).unwrap();
}
