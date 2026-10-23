use super::*;
use crate::application::projects::Projects;
use crate::application::worktrees::Worktrees;

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
        assert!(
            std::process::Command::new("git")
                .arg("-C")
                .arg(&path)
                .args(args)
                .status()
                .unwrap()
                .success()
        );
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
    remove_tree(&w).await.unwrap();
    std::fs::remove_dir_all(root).unwrap();
}

#[tokio::test]
async fn removal_preserves_locked_worktrees_and_submodule_storage_before_deleting_files() {
    let root = repo();
    let path = root.join("linked");
    std::fs::create_dir(&path).unwrap();
    allocate(&root, &path, "linked", "HEAD").await.unwrap();
    let w = Worktree {
        name: "linked".into(),
        path: path.to_string_lossy().into_owned(),
        repository: root.join(".git").to_string_lossy().into_owned(),
        ..Default::default()
    };
    let gitdir = metadata_paths(&path).unwrap().remove(0);
    std::fs::write(gitdir.join("locked"), "keep").unwrap();
    assert!(
        remove_tree(&w)
            .await
            .unwrap_err()
            .to_string()
            .contains("locked")
    );
    assert_eq!(
        std::fs::read_to_string(path.join("file")).unwrap(),
        "base\n"
    );
    std::fs::remove_file(gitdir.join("locked")).unwrap();

    std::fs::create_dir(gitdir.join("modules")).unwrap();
    std::fs::write(gitdir.join("modules/retained"), "keep").unwrap();
    assert!(
        remove_tree(&w)
            .await
            .unwrap_err()
            .to_string()
            .contains("submodules")
    );
    assert_eq!(
        std::fs::read_to_string(path.join("file")).unwrap(),
        "base\n"
    );
    assert_eq!(
        std::fs::read_to_string(gitdir.join("modules/retained")).unwrap(),
        "keep"
    );
    std::fs::remove_dir_all(gitdir.join("modules")).unwrap();

    let head = git(&path, &["rev-parse", "HEAD"]).await.unwrap();
    git(
        &path,
        &[
            "update-index",
            "--add",
            "--cacheinfo",
            &format!("160000,{head},submodule"),
        ],
    )
    .await
    .unwrap();
    assert!(
        remove_tree(&w)
            .await
            .unwrap_err()
            .to_string()
            .contains("submodules")
    );
    assert_eq!(
        std::fs::read_to_string(path.join("file")).unwrap(),
        "base\n"
    );
    git(&path, &["update-index", "--force-remove", "submodule"])
        .await
        .unwrap();
    remove_tree(&w).await.unwrap();
    assert!(!path.exists());
    std::fs::remove_dir_all(root).unwrap();
}

/// Exceed the general Git capture cap without requiring distinct blob contents.
fn commit_large_index(root: &Path) {
    use std::io::Write;
    use std::process::{Command, Stdio};

    let blob = Command::new("git")
        .arg("-C")
        .arg(root)
        .args(["rev-parse", "HEAD:file"])
        .output()
        .unwrap();
    assert!(blob.status.success());
    let blob = String::from_utf8(blob.stdout).unwrap();
    let mut input = String::new();
    for i in 0..6000 {
        input.push_str(&format!(
            "100644 {}\t{i:05}-{}\n",
            blob.trim(),
            "x".repeat(180)
        ));
    }
    assert!(input.len() > 1024 * 1024);
    let mut child = Command::new("git")
        .arg("-C")
        .arg(root)
        .args(["update-index", "--index-info"])
        .stdin(Stdio::piped())
        .spawn()
        .unwrap();
    child
        .stdin
        .take()
        .unwrap()
        .write_all(input.as_bytes())
        .unwrap();
    assert!(child.wait().unwrap().success());
    assert!(
        Command::new("git")
            .arg("-C")
            .arg(root)
            .args([
                "-c",
                "user.name=Test",
                "-c",
                "user.email=test@example.invalid",
                "commit",
                "-qm",
                "large index"
            ])
            .status()
            .unwrap()
            .success()
    );
}

#[tokio::test]
async fn removal_streams_large_clean_indexes_and_still_refuses_late_submodules() {
    let root = repo();
    let sibling = root.join("sibling");
    std::fs::create_dir(&sibling).unwrap();
    allocate(&root, &sibling, "sibling", "HEAD").await.unwrap();
    commit_large_index(&root);
    let path = root.join("large");
    std::fs::create_dir(&path).unwrap();
    allocate(&root, &path, "large", "HEAD").await.unwrap();
    assert!(
        git(&path, &["status", "--porcelain"])
            .await
            .unwrap()
            .is_empty()
    );
    // The previous full-index capture failed even though this checkout is clean.
    assert!(
        git(&path, &["ls-files", "--stage", "-z"])
            .await
            .unwrap_err()
            .to_string()
            .contains("output exceeded limit")
    );
    let w = Worktree {
        name: "large".into(),
        path: path.to_string_lossy().into_owned(),
        repository: root.join(".git").to_string_lossy().into_owned(),
        ..Default::default()
    };
    let head = git(&path, &["rev-parse", "HEAD"]).await.unwrap();
    git(
        &path,
        &[
            "update-index",
            "--add",
            "--cacheinfo",
            &format!("160000,{head},zz-last-submodule"),
        ],
    )
    .await
    .unwrap();
    assert!(
        remove_tree(&w)
            .await
            .unwrap_err()
            .to_string()
            .contains("submodules")
    );
    assert!(path.join("file").exists());
    git(
        &path,
        &["update-index", "--force-remove", "zz-last-submodule"],
    )
    .await
    .unwrap();
    remove_tree(&w).await.unwrap();
    assert!(!path.exists());
    assert_eq!(
        std::fs::read_to_string(sibling.join("file")).unwrap(),
        "base\n"
    );
    assert_eq!(
        git(&sibling, &["rev-parse", "--abbrev-ref", "HEAD"])
            .await
            .unwrap(),
        "sibling"
    );
    std::fs::remove_dir_all(root).unwrap();
}

#[tokio::test]
async fn metadata_paths_reports_unreadable_commondir() {
    let root = repo();
    let path = root.join("linked");
    std::fs::create_dir(&path).unwrap();
    allocate(&root, &path, "linked", "HEAD").await.unwrap();
    let gitdir = metadata_paths(&path).unwrap().remove(0);
    std::fs::remove_file(gitdir.join("commondir")).unwrap();
    std::fs::create_dir(gitdir.join("commondir")).unwrap();
    metadata_paths(&path).unwrap_err();
    std::fs::remove_dir_all(root).unwrap();
}

fn metadata_test_session() -> crate::config::SessionCfg {
    crate::config::SessionCfg {
        name: "metadata-test".into(),
        network: crate::config::NetworkMode::Host,
        cmd: Some("/usr/bin/true".into()),
        ..Default::default()
    }
}

#[tokio::test]
async fn primary_git_metadata_rejects_gitfiles_including_replaced_normal_checkout() {
    use crate::config::{Config, ProjectCfg};
    let root = repo();
    let foreign = repo();
    let linked = root.join("linked");
    let victim = foreign.join("victim");
    std::fs::create_dir(&linked).unwrap();
    std::fs::create_dir(&victim).unwrap();
    allocate(&root, &linked, "linked", "HEAD").await.unwrap();
    allocate(&foreign, &victim, "victim", "HEAD").await.unwrap();
    let cfg = Config::default();
    let session = metadata_test_session();
    let primary = ProjectCfg {
        name: "repo".into(),
        dir: root.to_string_lossy().into_owned(),
        ..Default::default()
    };
    // A normal primary still builds a launch, before its writable .git is replaced.
    let resolved = resolution::resolve(&Store::default(), &primary, "main")
        .await
        .unwrap();
    crate::sandbox::build_argv(&cfg, &session, &resolved).unwrap();
    let pointer = std::fs::read(victim.join(".git")).unwrap();
    std::fs::write(linked.join(".git"), &pointer).unwrap();
    std::fs::rename(root.join(".git"), root.join("original-metadata")).unwrap();
    std::fs::write(root.join(".git"), &pointer).unwrap();
    for path in [&root, &linked] {
        let project = ProjectCfg {
            dir: path.to_string_lossy().into_owned(),
            ..primary.clone()
        };
        for id in ["", "main"] {
            resolution::resolve(&Store::default(), &project, id)
                .await
                .unwrap_err();
        }
        // Includes callers which bypass resolution entirely.
        let error = crate::sandbox::build_argv(&cfg, &session, &project).unwrap_err();
        assert!(
            error
                .to_string()
                .contains("primary checkout .git must be a directory")
        );
    }
    // Earlier successful resolution cannot authorize a newly installed pointer.
    crate::sandbox::build_argv(&cfg, &session, &resolved).unwrap_err();
    std::fs::remove_dir_all(root).unwrap();
    std::fs::remove_dir_all(foreign).unwrap();
}

#[tokio::test]
async fn registered_git_metadata_revalidates_gitdir_and_common_at_mount_construction() {
    use crate::config::{Config, ProjectCfg};
    let root = repo();
    let foreign = repo();
    let linked = root.join("linked");
    let victim = foreign.join("victim");
    std::fs::create_dir(&linked).unwrap();
    std::fs::create_dir(&victim).unwrap();
    allocate(&root, &linked, "linked", "HEAD").await.unwrap();
    allocate(&foreign, &victim, "victim", "HEAD").await.unwrap();
    let repository = root.join(".git").canonicalize().unwrap();
    let gitdir = metadata_paths(&linked).unwrap().remove(0);
    let foreign_gitdir = metadata_paths(&victim).unwrap().remove(0);
    let primary = ProjectCfg {
        id: "project".into(),
        name: "repo".into(),
        dir: root.to_string_lossy().into_owned(),
        ..Default::default()
    };
    let store = Store {
        worktrees: vec![Worktree {
            id: "linked".into(),
            project_id: primary.id.clone(),
            path: linked.to_string_lossy().into_owned(),
            repository: repository.to_string_lossy().into_owned(),
            phase: WorktreePhase::Ready,
            ..Default::default()
        }],
    };
    let effective = resolution::resolve(&store, &primary, "linked")
        .await
        .unwrap();
    let cfg = Config::default();
    let session = metadata_test_session();
    let argv = crate::sandbox::build_argv(&cfg, &session, &effective).unwrap();
    for expected in [&gitdir, &repository] {
        assert!(argv.windows(3).any(|args| args[0] == "--bind"
            && Path::new(&args[1]) == expected
            && args[1] == args[2]));
    }
    // A project's registered checkout may also be the repository's normal checkout.
    let mut original_store = store.clone();
    original_store.worktrees[0].path = primary.dir.clone();
    let linked_primary = ProjectCfg {
        dir: linked.to_string_lossy().into_owned(),
        ..primary.clone()
    };
    let original = resolution::resolve(&original_store, &linked_primary, "linked")
        .await
        .unwrap();
    crate::sandbox::build_argv(&cfg, &session, &original).unwrap();
    let pointer = std::fs::read(linked.join(".git")).unwrap();
    let common = std::fs::read(gitdir.join("commondir")).unwrap();
    // Foreign gitdir, but the expected common repository: check both independently.
    std::fs::write(
        foreign_gitdir.join("commondir"),
        repository.to_str().unwrap(),
    )
    .unwrap();
    std::fs::copy(victim.join(".git"), linked.join(".git")).unwrap();
    crate::sandbox::build_argv(&cfg, &session, &effective).unwrap_err();
    resolution::resolve(&store, &primary, "linked")
        .await
        .unwrap_err();
    // Expected gitdir, but foreign common repository.
    std::fs::write(linked.join(".git"), pointer).unwrap();
    std::fs::write(
        gitdir.join("commondir"),
        foreign.join(".git").to_str().unwrap(),
    )
    .unwrap();
    crate::sandbox::build_argv(&cfg, &session, &effective).unwrap_err();
    resolution::resolve(&store, &primary, "linked")
        .await
        .unwrap_err();
    std::fs::write(gitdir.join("commondir"), common).unwrap();
    crate::sandbox::build_argv(&cfg, &session, &effective).unwrap();
    std::fs::remove_dir_all(root).unwrap();
    std::fs::remove_dir_all(foreign).unwrap();
}

struct LifecycleFixture {
    root: PathBuf,
    manager: std::sync::Arc<crate::session::SessionRuntime>,
    worktree: Worktree,
}

#[tokio::test]
async fn removal_blocks_optional_filters_targeting_siblings_including_attached_sessions() {
    use crate::application::worktrees::WorktreeRequest;
    use crate::config::SessionCfg;

    for attached in [false, true] {
        let LifecycleFixture {
            root,
            manager,
            worktree,
        } = Box::pin(lifecycle_fixture()).await;
        let application = crate::application::Application::from_runtime(&manager);
        let worktrees = Worktrees::new(&application);
        let sibling = Box::pin(worktrees.create_worktree(WorktreeRequest {
            project: "repo".into(),
            name: "sibling".into(),
            ..Default::default()
        }))
        .await
        .unwrap();
        if attached {
            manager
                .edits()
                .add(SessionCfg {
                    name: "attached".into(),
                    project: "repo".into(),
                    worktree: sibling.id.clone(),
                    cmd: Some("true".into()),
                    ..Default::default()
                })
                .await
                .unwrap();
        }
        let checkout = Path::new(&worktree.path);
        let victim = Path::new(&sibling.path).join("file");
        let sibling_index = metadata_paths(Path::new(&sibling.path)).unwrap()[0].join("index");
        let original_index = std::fs::read(&sibling_index).unwrap();
        std::fs::write(root.join(".git/info/attributes"), "file filter=attack\n").unwrap();
        // Optional failure preserves the original bytes during restricted inspection.
        // An ordinary status really executes this same helper and alters the sibling.
        let helper = format!("printf compromised > '{}'; cat", victim.display());
        git(&root, &["config", "filter.attack.clean", &helper])
            .await
            .unwrap();
        let invalidate = || {
            std::fs::File::options()
                .write(true)
                .open(checkout.join("file"))
                .unwrap()
                .set_times(
                    std::fs::FileTimes::new().set_modified(std::time::SystemTime::UNIX_EPOCH),
                )
                .unwrap();
        };
        invalidate();
        let status = std::process::Command::new("git")
            .arg("-C")
            .arg(checkout)
            .args(["status", "--porcelain"])
            .env("GIT_OPTIONAL_LOCKS", "0")
            .output()
            .unwrap();
        assert!(status.status.success());
        assert!(status.stdout.is_empty());
        assert_eq!(std::fs::read_to_string(&victim).unwrap(), "compromised");
        std::fs::write(&victim, "base\n").unwrap();
        invalidate();

        git(&root, &["config", "filter.attack.required", "true"])
            .await
            .unwrap();
        assert!(
            worktrees
                .remove_worktree("repo".into(), worktree.id.clone())
                .await
                .is_err()
        );
        assert_eq!(
            std::fs::read_to_string(checkout.join("file")).unwrap(),
            "base\n"
        );
        assert_eq!(std::fs::read_to_string(&victim).unwrap(), "base\n");
        git(&root, &["config", "filter.attack.required", "false"])
            .await
            .unwrap();
        worktrees
            .remove_worktree("repo".into(), worktree.id)
            .await
            .unwrap();
        assert!(!checkout.exists());
        assert_eq!(std::fs::read_to_string(&victim).unwrap(), "base\n");
        assert_eq!(std::fs::read(&sibling_index).unwrap(), original_index);
        let registered = git(&root, &["worktree", "list", "--porcelain"])
            .await
            .unwrap();
        assert!(!registered.contains(&format!("worktree {}\n", worktree.path)));
        assert!(registered.contains(&format!("worktree {}\n", sibling.path)));
        assert_eq!(
            worktrees.worktree_list("repo").await.unwrap()[1]
                .attachments
                .len(),
            usize::from(attached)
        );
        std::fs::remove_dir_all(root).unwrap();
    }
}

async fn lifecycle_fixture() -> LifecycleFixture {
    use crate::application::worktrees::WorktreeRequest;
    use crate::config::{Config, ProjectCfg};
    use crate::session::test_manager;
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
    let worktree = Box::pin(
        Worktrees::new(&crate::application::Application::from_runtime(&manager)).create_worktree(
            WorktreeRequest {
                project: "repo".into(),
                name: "parallel".into(),
                ..Default::default()
            },
        ),
    )
    .await
    .unwrap();
    assert_eq!(Path::new(&worktree.path).file_name().unwrap(), "parallel");
    assert_eq!(
        Path::new(&worktree.path)
            .parent()
            .unwrap()
            .file_name()
            .unwrap(),
        "repo"
    );
    Worktrees::new(&crate::application::Application::from_runtime(&manager))
        .rename_worktree("repo".into(), worktree.id.clone(), "../escape".into())
        .await
        .unwrap_err();
    let occupied = Path::new(&worktree.path).parent().unwrap().join("occupied");
    std::fs::create_dir(&occupied).unwrap();
    Worktrees::new(&crate::application::Application::from_runtime(&manager))
        .rename_worktree("repo".into(), worktree.id.clone(), "occupied".into())
        .await
        .unwrap_err();
    assert!(Path::new(&worktree.path).exists());
    let old_path = worktree.path.clone();
    let worktree = Worktrees::new(&crate::application::Application::from_runtime(&manager))
        .rename_worktree("repo".into(), worktree.id.clone(), "feature name".into())
        .await
        .unwrap();
    assert_eq!(
        Path::new(&worktree.path).file_name().unwrap(),
        "feature name"
    );
    assert!(!Path::new(&old_path).exists());
    assert_eq!(
        git(Path::new(&worktree.path), &["rev-parse", "--show-toplevel"])
            .await
            .unwrap(),
        worktree.path
    );
    assert_eq!(
        std::fs::read_to_string(Path::new(&worktree.path).join("file")).unwrap(),
        "base\n"
    );
    assert_eq!(
        std::fs::read_to_string(root.join("file")).unwrap(),
        "caller dirty\n"
    );
    assert!(
        manager
            .task_store()
            .all_tasks_async()
            .await
            .unwrap()
            .is_empty()
    );
    assert!(manager.config().await.sessions.is_empty());
    assert!(
        Projects::new(&crate::application::Application::from_runtime(&manager))
            .remove_project("repo")
            .await
            .is_err()
    );
    LifecycleFixture {
        root,
        manager,
        worktree,
    }
}

#[tokio::test]
async fn lifecycle_is_independent_and_removal_preserves_branches() {
    use crate::config::SessionCfg;
    let LifecycleFixture {
        root,
        manager,
        worktree: w,
    } = Box::pin(lifecycle_fixture()).await;

    // Stopped sessions also count as attachments. Removing either attachment preserves the checkout.
    for name in ["one", "two"] {
        manager
            .edits()
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
        Worktrees::new(&crate::application::Application::from_runtime(&manager))
            .worktree_list("repo")
            .await
            .unwrap()[1]
            .attachments
            .len(),
        2
    );
    Worktrees::new(&crate::application::Application::from_runtime(&manager))
        .rename_worktree("repo".into(), w.id.clone(), "blocked".into())
        .await
        .unwrap_err();
    assert!(
        Worktrees::new(&crate::application::Application::from_runtime(&manager))
            .remove_worktree("repo".into(), w.id.clone())
            .await
            .is_err()
    );
    manager.edits().remove("one").await.unwrap();
    assert!(Path::new(&w.path).exists());
    manager.edits().remove("two").await.unwrap();
    assert!(Path::new(&w.path).exists());

    // Failed removal neither commits nor destroys files. Retry uses the actual current branch.
    std::fs::write(Path::new(&w.path).join("untracked"), "keep me").unwrap();
    assert!(
        Worktrees::new(&crate::application::Application::from_runtime(&manager))
            .remove_worktree("repo".into(), w.id.clone())
            .await
            .is_err()
    );
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
    let rows = Worktrees::new(&crate::application::Application::from_runtime(&manager))
        .worktree_list("repo")
        .await
        .unwrap();
    assert_eq!(rows[1].branch, "switched");
    assert_eq!(rows[1].worktree.id, w.id);
    Worktrees::new(&crate::application::Application::from_runtime(&manager))
        .remove_worktree("repo".into(), w.id)
        .await
        .unwrap();
    assert!(!Path::new(&w.path).exists());
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
    use crate::application::worktrees::WorktreeRequest;
    use crate::config::{Config, ProjectCfg};
    use crate::session::test_manager;
    let root = repo();
    let mut cfg = Config::default();
    cfg.projects.push(ProjectCfg {
        name: "repo".into(),
        dir: root.to_string_lossy().into_owned(),
        worktree_root: root.join("trees").to_string_lossy().into_owned(),
        ..Default::default()
    });
    let manager = test_manager(cfg);
    let w = Box::pin(
        Worktrees::new(&crate::application::Application::from_runtime(&manager)).create_worktree(
            WorktreeRequest {
                project: "repo".into(),
                ..Default::default()
            },
        ),
    )
    .await
    .unwrap();
    let path = Path::new(&w.path);
    // Ignored data is not assumed to be disposable build output.
    std::fs::write(root.join(".git/info/exclude"), "precious\n").unwrap();
    std::fs::write(path.join("precious"), "not committed").unwrap();
    assert!(
        Worktrees::new(&crate::application::Application::from_runtime(&manager))
            .remove_worktree("repo".into(), w.id.clone())
            .await
            .unwrap_err()
            .to_string()
            .contains("ignored")
    );
    assert!(path.join("precious").exists());
    std::fs::remove_file(path.join("precious")).unwrap();
    // Agent-controlled detached HEAD is valid, but new unreachable work blocks teardown.
    std::fs::write(path.join(".git"), std::fs::read(path.join(".git")).unwrap()).unwrap();
    let metadata = metadata_paths(path).unwrap();
    std::fs::write(metadata[0].join("HEAD"), format!("{}\n", w.base)).unwrap();
    std::fs::write(path.join("file"), "detached change\n").unwrap();
    assert!(
        std::process::Command::new("git")
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
            .success()
    );
    assert!(
        Worktrees::new(&crate::application::Application::from_runtime(&manager))
            .remove_worktree("repo".into(), w.id.clone())
            .await
            .unwrap_err()
            .to_string()
            .contains("retained local branch")
    );
    assert_eq!(
        Worktrees::new(&crate::application::Application::from_runtime(&manager))
            .worktree_list("repo")
            .await
            .unwrap()[1]
            .branch,
        ""
    );
    // Restart recovery reports an interrupted operation without committing, deleting or relaunching.
    let mut store = manager.worktree_records();
    store.worktrees[0].phase = crate::worktrees::WorktreePhase::Removing;
    manager.save_worktrees(&store).await.unwrap();
    Worktrees::new(&crate::application::Application::from_runtime(&manager))
        .recover_worktrees()
        .await
        .unwrap();
    let recovered = manager.worktree_records();
    assert_eq!(
        recovered.worktrees[0].phase,
        crate::worktrees::WorktreePhase::Error
    );
    assert!(path.join("file").exists());
    assert!(manager.config().await.sessions.is_empty());
    std::fs::remove_dir_all(root).unwrap();
}

#[tokio::test]
async fn managed_worktree_branches_match_names_and_validate_before_allocation() {
    use crate::config::{Config, ProjectCfg};
    use crate::session::test_manager;
    let root = repo();
    let manager = test_manager(Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: root.to_string_lossy().into_owned(),
            ..Default::default()
        }],
        ..Default::default()
    });
    Box::pin(assert_rejected_managed_worktree_names(&manager, &root)).await;
    let (named, generated) = Box::pin(create_managed_worktrees(&manager, &root)).await;
    let renamed = rename_project_worktree(&manager, &root, &named).await;

    Worktrees::new(&crate::application::Application::from_runtime(&manager))
        .remove_worktree("renamed".into(), renamed.id.clone())
        .await
        .unwrap();
    assert!(!Path::new(&renamed.path).exists());
    assert!(root.join("file").exists());
    assert!(Path::new(&generated.path).join("file").exists());
    std::fs::remove_dir_all(root).unwrap();
}

async fn assert_rejected_managed_worktree_names(
    manager: &std::sync::Arc<crate::session::SessionRuntime>,
    root: &Path,
) {
    use crate::application::worktrees::WorktreeRequest;
    for name in ["invalid name", "bad..name", "HEAD", "-option", "@{-1}"] {
        let invalid = Box::pin(
            Worktrees::new(&crate::application::Application::from_runtime(manager))
                .create_worktree(WorktreeRequest {
                    project: "repo".into(),
                    name: name.into(),
                    ..Default::default()
                }),
        )
        .await
        .unwrap_err();
        assert!(invalid.to_string().contains("invalid Git branch name"));
        assert!(!root.join(".worktrees").exists());
    }

    assert!(
        std::process::Command::new("git")
            .arg("-C")
            .arg(root)
            .args(["branch", "already-exists"])
            .status()
            .unwrap()
            .success()
    );
    let collision = Box::pin(
        Worktrees::new(&crate::application::Application::from_runtime(manager)).create_worktree(
            WorktreeRequest {
                project: "repo".into(),
                name: "already-exists".into(),
                ..Default::default()
            },
        ),
    )
    .await
    .unwrap_err();
    assert!(collision.to_string().contains("already exists"));
    assert!(!root.join(".worktrees").exists());
    assert!(manager.worktree_records().worktrees.is_empty());
}

async fn create_managed_worktrees(
    manager: &std::sync::Arc<crate::session::SessionRuntime>,
    root: &Path,
) -> (Worktree, Worktree) {
    use crate::application::worktrees::WorktreeRequest;
    let named = Box::pin(
        Worktrees::new(&crate::application::Application::from_runtime(manager)).create_worktree(
            WorktreeRequest {
                project: "repo".into(),
                name: "feature-branch".into(),
                ..Default::default()
            },
        ),
    )
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

    let generated = Box::pin(
        Worktrees::new(&crate::application::Application::from_runtime(manager)).create_worktree(
            WorktreeRequest {
                project: "repo".into(),
                ..Default::default()
            },
        ),
    )
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
    (named, generated)
}

async fn rename_project_worktree(
    manager: &std::sync::Arc<crate::session::SessionRuntime>,
    root: &Path,
    named: &Worktree,
) -> Worktree {
    let mut project = manager.config().await.projects[0].clone();
    project.name = "renamed".into();
    Projects::new(&crate::application::Application::from_runtime(manager))
        .update_project("repo", project)
        .await
        .unwrap();
    assert_eq!(
        Worktrees::new(&crate::application::Application::from_runtime(manager))
            .worktree_list("renamed")
            .await
            .unwrap()[1]
            .worktree
            .path,
        named.path
    );
    let renamed = Worktrees::new(&crate::application::Application::from_runtime(manager))
        .rename_worktree("renamed".into(), named.id.clone(), "new-name".into())
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
    renamed
}

#[tokio::test]
async fn external_checkouts_and_interrupted_teardown_have_independent_records() {
    use crate::application::worktrees::WorktreeRequest;
    use crate::config::{Config, ProjectCfg};
    use crate::session::test_manager;
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
    let worktrees = Worktrees::new(&crate::application::Application::from_runtime(&manager));
    let projects = Projects::new(&crate::application::Application::from_runtime(&manager));
    let w = Box::pin(worktrees.create_worktree(WorktreeRequest {
        project: "repo".into(),
        ..Default::default()
    }))
    .await
    .unwrap();
    let mut store = manager.worktree_records();
    store.worktrees[0].phase = crate::worktrees::WorktreePhase::Allocating;
    manager.save_worktrees(&store).await.unwrap();
    worktrees.recover_worktrees().await.unwrap();
    assert_eq!(
        manager.worktree_records().worktrees[0].phase,
        crate::worktrees::WorktreePhase::Ready
    );
    let mut project = manager.config().await.projects[0].clone();
    project.name = "renamed".into();
    projects.update_project("repo", project).await.unwrap();
    let renamed_path = worktrees.worktree_list("renamed").await.unwrap()[1]
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
        crate::application::file_actions::worktree_for_path(
            &Worktrees::new(&crate::application::Application::from_runtime(&manager)),
            "renamed",
            &format!("{renamed_path}/file")
        )
        .await
        .unwrap(),
        w.id
    );
    // Simulate completed Git removal before the daemon saves the result.
    // Recovery does not delete files. A retry can finish without an existing checkout or nested sandbox.
    assert!(
        std::process::Command::new("git")
            .arg("-C")
            .arg(&root)
            .args(["worktree", "remove", &renamed_path])
            .status()
            .unwrap()
            .success()
    );
    worktrees
        .remove_worktree("renamed".into(), w.id)
        .await
        .unwrap();
    assert_eq!(worktrees.worktree_list("renamed").await.unwrap().len(), 1);
    let external = root.join("external checkout");
    std::fs::create_dir(&external).unwrap();
    let base = git(&root, &["rev-parse", "HEAD"]).await.unwrap();
    allocate(&root, &external, "external", &base).await.unwrap();
    let w = Box::pin(worktrees.create_worktree(WorktreeRequest {
        project: "renamed".into(),
        path: external.to_string_lossy().into_owned(),
        ..Default::default()
    }))
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
    worktrees
        .remove_worktree("renamed".into(), w.id)
        .await
        .unwrap();
    assert!(external.join("file").exists());
    assert!(
        worktrees
            .remove_worktree("renamed".into(), "main".into())
            .await
            .is_err()
    );
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
    result.unwrap_err();
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
    use crate::application::worktrees::WorktreeRequest;
    use crate::config::{Config, Mount, MountMode, NetworkMode, ProjectCfg, SessionCfg};
    use crate::session::test_manager;
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
    let w = Box::pin(
        Worktrees::new(&crate::application::Application::from_runtime(&manager)).create_worktree(
            WorktreeRequest {
                project: "repo".into(),
                ..Default::default()
            },
        ),
    )
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
    let args = crate::sandbox::build_argv(&cfg, &session, &effective).unwrap();
    assert!(
        args.windows(3)
            .any(|v| v[0] == "--bind" && v[1] == w.repository && v[2] == w.repository)
    );
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
    manager.resolve_worktree(&other, &w.id).await.unwrap_err();
    let mut unsafe_mount = project.clone();
    unsafe_mount.mounts.push(Mount {
        from: root.to_string_lossy().into_owned(),
        to: root.to_string_lossy().into_owned(),
        mode: MountMode::Ro,
    });
    manager
        .resolve_worktree(&unsafe_mount, &w.id)
        .await
        .unwrap_err();
    assert!(cache.exists());
    std::fs::write(cache.join("artifact"), "kept").unwrap();
    Worktrees::new(&crate::application::Application::from_runtime(&manager))
        .remove_worktree("repo".into(), w.id)
        .await
        .unwrap();
    assert!(!Path::new(&w.path).exists());
    assert_eq!(
        std::fs::read_to_string(cache.join("artifact")).unwrap(),
        "kept"
    );
    std::fs::remove_dir_all(root).unwrap();
    std::fs::remove_dir_all(cache).unwrap();
}

#[tokio::test]
async fn relocation_rollback_restores_checkout_registration_and_catalog() {
    use std::os::unix::ffi::OsStringExt;
    let root = repo();
    let source = root.join("source");
    std::fs::create_dir(&source).unwrap();
    allocate(&root, &source, "source", "HEAD").await.unwrap();
    let original = Worktree {
        id: "1111111111111111".into(),
        project_id: "2222222222222222".into(),
        name: "source".into(),
        path: source.to_string_lossy().into_owned(),
        repository: root.join(".git").to_string_lossy().into_owned(),
        phase: crate::worktrees::WorktreePhase::Ready,
        managed: true,
        ..Default::default()
    };
    let invalid = root.join(std::ffi::OsString::from_vec(vec![b'x', 0xff]));
    assert!(relocate_tree(&original, &invalid).await.is_err());
    assert!(source.exists());
    let manager = crate::session::test_manager(crate::config::Config::default());
    let mut store = Store::default();
    store.worktrees.push(original.clone());
    manager.save_worktrees(&store).await.unwrap();
    let destination = root.join("destination");
    let mut moved = original.clone();
    moved.name = "destination".into();
    moved.path = destination.to_string_lossy().into_owned();
    let mut relocation = relocation::Relocations::new(store, vec![(0, moved.clone())])
        .expect("test relocation uses a catalog entry that exists");
    relocation
        .execute(&Worktrees::new(
            &crate::application::Application::from_runtime(&manager),
        ))
        .await
        .unwrap();
    assert_eq!(manager.worktree_records().worktrees[0].path, moved.path);
    relocation
        .rollback(&Worktrees::new(
            &crate::application::Application::from_runtime(&manager),
        ))
        .await
        .unwrap();
    assert!(!destination.exists());
    assert_eq!(manager.worktree_records().worktrees[0].path, original.path);
    assert_eq!(
        git(&source, &["rev-parse", "--show-toplevel"])
            .await
            .unwrap(),
        original.path
    );
    // Repeated cleanup remains safe after all moves have been restored.
    relocation
        .rollback(&Worktrees::new(
            &crate::application::Application::from_runtime(&manager),
        ))
        .await
        .unwrap();
    std::fs::remove_dir_all(root).unwrap();
}
