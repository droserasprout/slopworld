use super::*;

#[test]
fn porcelain_reads_the_pair_and_the_path() {
    let rows = parse_porcelain(" M src/api.rs\0?? notes.txt\0M  Cargo.toml\0");
    assert_eq!(
        rows,
        vec![
            ("src/api.rs".to_string(), " M".to_string()),
            ("notes.txt".to_string(), "??".to_string()),
            ("Cargo.toml".to_string(), "M ".to_string()),
        ]
    );
}

#[test]
fn porcelain_keeps_a_leading_space_in_the_filename() {
    let rows = parse_porcelain("??  notes.txt\0");
    assert_eq!(rows, vec![(" notes.txt".to_string(), "??".to_string())]);
}

#[test]
fn a_rename_is_one_row_and_the_old_name_is_dropped() {
    let rows = parse_porcelain("R  new.rs\0old.rs\0 M after.rs\0");
    assert_eq!(
        rows,
        vec![
            ("new.rs".to_string(), "R ".to_string()),
            ("after.rs".to_string(), " M".to_string()),
        ]
    );
}

#[test]
fn numstat_counts_and_a_binary_file_counts_nothing() {
    let map = parse_numstat("3\t1\tsrc/api.rs\0-\t-\tlogo.png\0");
    assert_eq!(map["src/api.rs"], (Some(3), Some(1)));
    assert_eq!(map["logo.png"], (None, None));
}

#[test]
fn numstat_takes_the_new_name_of_a_rename() {
    let map = parse_numstat("2\t0\t\0old.rs\0new.rs\0");
    assert_eq!(map["new.rs"], (Some(2), Some(0)));
    assert!(!map.contains_key("old.rs"));
}

#[test]
fn numstat_keeps_tabs_in_a_filename() {
    let map = parse_numstat("2\t0\ttab\tname.txt\0");
    assert_eq!(map["tab\tname.txt"], (Some(2), Some(0)));
}

#[tokio::test]
async fn a_pure_rename_keeps_zero_line_counts() {
    let dir = std::env::temp_dir().join(format!("slopd-git-rename-{}", std::process::id()));
    drop(tokio::fs::remove_dir_all(&dir).await);
    tokio::fs::create_dir_all(&dir).await.unwrap();
    tokio::fs::write(dir.join("old.txt"), "unchanged\n")
        .await
        .unwrap();

    let init = Command::new("git")
        .args(["init", "-q"])
        .current_dir(&dir)
        .status()
        .await
        .unwrap();
    assert!(init.success());
    let add = Command::new("git")
        .args(["add", "old.txt"])
        .current_dir(&dir)
        .status()
        .await
        .unwrap();
    assert!(add.success());
    let commit = Command::new("git")
        .args([
            "-c",
            "user.name=slopd-test",
            "-c",
            "user.email=slopd-test@example.invalid",
            "-c",
            "commit.gpgsign=false",
            "commit",
            "-qm",
            "initial",
        ])
        .current_dir(&dir)
        .status()
        .await
        .unwrap();
    assert!(commit.success());
    let rename = Command::new("git")
        .args(["mv", "old.txt", "new.txt"])
        .current_dir(&dir)
        .status()
        .await
        .unwrap();
    assert!(rename.success());

    let answer = status(&dir).await.unwrap().unwrap();
    assert_eq!(answer.added, 0);
    assert_eq!(answer.deleted, 0);
    assert_eq!(answer.changes.len(), 1);
    let change = &answer.changes[0];
    assert_eq!(change.path, "new.txt");
    assert_eq!(change.status, "R ");
    assert_eq!(change.added, Some(0));
    assert_eq!(change.deleted, Some(0));

    tokio::fs::remove_dir_all(&dir).await.unwrap();
}

#[tokio::test]
async fn a_directory_that_is_no_repository_is_not_an_error() {
    let dir = std::env::temp_dir().join("slopd-git-none");
    drop(tokio::fs::create_dir_all(&dir).await);
    // Only meaningful where the temp dir is not itself inside a checkout, which is the
    // usual arrangement. A machine where it is would make this vacuous rather than wrong.
    if let Ok(answer) = status(&dir).await {
        assert!(answer.is_none() || answer.unwrap().root != dir);
    }
}

#[tokio::test]
async fn an_untracked_directory_reports_its_files() {
    let dir = std::env::temp_dir().join(format!("slopd-git-untracked-{}", std::process::id()));
    let nested = dir.join("untracked/nested");
    tokio::fs::create_dir_all(&nested).await.unwrap();
    tokio::fs::write(nested.join("file.txt"), "hello\n")
        .await
        .unwrap();
    tokio::fs::write(nested.join("binary.bin"), [0_u8, 1_u8, 2_u8])
        .await
        .unwrap();
    tokio::fs::write(nested.join("tab\tname.txt"), "one\ntwo\n")
        .await
        .unwrap();
    tokio::fs::write(dir.join("staged.txt"), "before\n")
        .await
        .unwrap();

    let init = Command::new("git")
        .arg("init")
        .arg("-q")
        .current_dir(&dir)
        .status()
        .await
        .unwrap();
    assert!(init.success());

    let add = Command::new("git")
        .args(["add", "staged.txt"])
        .current_dir(&dir)
        .status()
        .await
        .unwrap();
    assert!(add.success());
    tokio::fs::write(dir.join("staged.txt"), "before\nafter\n")
        .await
        .unwrap();

    let paths = status_with_counts(&dir, false).await.unwrap().unwrap();
    assert_eq!(paths.changes.len(), 4);
    assert!(!paths.counts_complete);
    assert!(paths
        .changes
        .iter()
        .all(|c| c.added.is_none() && c.deleted.is_none()));

    let answer = status(&dir).await.unwrap().unwrap();
    assert!(answer.counts_complete);
    assert_eq!(answer.changes.len(), 4);
    let text = answer
        .changes
        .iter()
        .find(|change| change.path == "untracked/nested/file.txt")
        .unwrap();
    assert_eq!(text.status, "??");
    assert_eq!(text.added, Some(1));
    assert_eq!(text.deleted, Some(0));
    let binary = answer
        .changes
        .iter()
        .find(|change| change.path == "untracked/nested/binary.bin")
        .unwrap();
    assert_eq!(binary.added, None);
    assert_eq!(binary.deleted, None);
    let tabbed = answer
        .changes
        .iter()
        .find(|change| change.path == "untracked/nested/tab\tname.txt")
        .unwrap();
    assert_eq!(tabbed.added, Some(2));
    assert_eq!(tabbed.deleted, Some(0));
    let staged = answer
        .changes
        .iter()
        .find(|change| change.path == "staged.txt")
        .unwrap();
    assert_eq!(staged.status, "AM");
    assert_eq!(staged.added, Some(2));
    assert_eq!(staged.deleted, Some(0));
    assert_eq!(answer.added, 5);
    assert_eq!(answer.deleted, 0);

    tokio::fs::remove_dir_all(&dir).await.unwrap();
}

#[tokio::test]
async fn a_nested_repository_does_not_add_its_worktree_to_the_parent() {
    let dir = std::env::temp_dir().join(format!("slopd-git-nested-{}", std::process::id()));
    let nested = dir.join("nested");
    drop(tokio::fs::remove_dir_all(&dir).await);
    tokio::fs::create_dir_all(&nested).await.unwrap();

    for path in [&dir, &nested] {
        let init = Command::new("git")
            .args(["init", "-q"])
            .current_dir(path)
            .status()
            .await
            .unwrap();
        assert!(init.success());
    }

    tokio::fs::write(nested.join("tracked.txt"), "before\n")
        .await
        .unwrap();
    let commit = Command::new("git")
        .args([
            "-c",
            "user.name=slopd-test",
            "-c",
            "user.email=slopd-test@example.invalid",
            "add",
            "tracked.txt",
        ])
        .current_dir(&nested)
        .status()
        .await
        .unwrap();
    assert!(commit.success());
    let commit = Command::new("git")
        .args([
            "-c",
            "user.name=slopd-test",
            "-c",
            "user.email=slopd-test@example.invalid",
            "-c",
            "commit.gpgsign=false",
            "commit",
            "-qm",
            "initial",
        ])
        .current_dir(&nested)
        .status()
        .await
        .unwrap();
    assert!(commit.success());
    tokio::fs::write(nested.join("tracked.txt"), "before\nafter\n")
        .await
        .unwrap();

    let add = Command::new("git")
        .args(["-c", "advice.addEmbeddedRepo=false", "add", "nested"])
        .current_dir(&dir)
        .stderr(Stdio::null())
        .status()
        .await
        .unwrap();
    assert!(add.success());

    let answer = status(&dir).await.unwrap().unwrap();
    assert_eq!(answer.changes.len(), 1);
    assert_eq!(answer.changes[0].path, "nested");
    assert_eq!(answer.changes[0].status, "A ");

    tokio::fs::remove_dir_all(&dir).await.unwrap();
}

#[tokio::test]
async fn an_untracked_nested_repository_stays_a_boundary_row() {
    let dir =
        std::env::temp_dir().join(format!("slopd-git-untracked-nested-{}", std::process::id()));
    let nested = dir.join("outer/nested");
    drop(tokio::fs::remove_dir_all(&dir).await);
    tokio::fs::create_dir_all(&nested).await.unwrap();

    for path in [&dir, &nested] {
        let init = Command::new("git")
            .args(["init", "-q"])
            .current_dir(path)
            .status()
            .await
            .unwrap();
        assert!(init.success());
    }
    tokio::fs::write(nested.join("inside.txt"), "nested\n")
        .await
        .unwrap();

    let answer = status(&dir).await.unwrap().unwrap();
    assert!(answer
        .changes
        .iter()
        .any(|change| change.path == "outer/nested/"));
    assert!(!answer
        .changes
        .iter()
        .any(|change| change.path == "outer/nested/inside.txt"));

    tokio::fs::remove_dir_all(&dir).await.unwrap();
}

#[tokio::test]
async fn repository_helpers_cannot_execute_git_config_during_status_or_counts() {
    let dir = std::env::temp_dir().join(format!(
        "slopd-git-policy-{}-{}",
        std::process::id(),
        uuid::Uuid::new_v4()
    ));
    let marker = dir.with_extension("marker");
    seed_repository_with_git_helpers(&dir, &marker).await;
    assert_configured_repository_status(&dir, &marker).await;

    let unborn = dir.join("unborn");
    let unborn_marker = unborn.with_extension("marker");
    seed_unborn_repository_with_git_helper(&unborn, &unborn_marker).await;
    assert_unborn_repository_status(&unborn, &unborn_marker).await;
    tokio::fs::remove_dir_all(&dir).await.unwrap();
}

async fn seed_repository_with_git_helpers(dir: &Path, marker: &Path) {
    tokio::fs::create_dir_all(dir).await.unwrap();
    let init = Command::new("git")
        .args(["init", "-q"])
        .current_dir(dir)
        .status()
        .await
        .unwrap();
    assert!(init.success());
    for (key, value) in [
        ("user.name", "slopd-test"),
        ("user.email", "slopd-test@example.invalid"),
    ] {
        let configured = Command::new("git")
            .args(["config", key, value])
            .current_dir(dir)
            .status()
            .await
            .unwrap();
        assert!(configured.success());
    }
    tokio::fs::write(dir.join("tracked.txt"), "before\n")
        .await
        .unwrap();
    tokio::fs::write(dir.join(".gitattributes"), "* diff=marker\n")
        .await
        .unwrap();
    let add = Command::new("git")
        .args(["add", "."])
        .current_dir(dir)
        .status()
        .await
        .unwrap();
    assert!(add.success());
    let commit = Command::new("git")
        .args(["-c", "commit.gpgsign=false", "commit", "-qm", "initial"])
        .current_dir(dir)
        .status()
        .await
        .unwrap();
    assert!(commit.success());

    for (key, value) in [
        ("core.fsmonitor", format!("touch {}", marker.display())),
        ("diff.external", format!("touch {}", marker.display())),
        (
            "diff.marker.textconv",
            format!("touch {}", marker.display()),
        ),
    ] {
        let configured = Command::new("git")
            .args(["config", key, &value])
            .current_dir(dir)
            .status()
            .await
            .unwrap();
        assert!(configured.success());
    }
    tokio::fs::write(dir.join("tracked.txt"), "before\nafter\n")
        .await
        .unwrap();
    tokio::fs::write(dir.join("staged.txt"), "staged\n")
        .await
        .unwrap();
    let add = Command::new("git")
        .args(["-c", "core.fsmonitor=false", "add", "staged.txt"])
        .current_dir(dir)
        .status()
        .await
        .unwrap();
    assert!(add.success());
    drop(tokio::fs::remove_file(marker).await);
}

async fn assert_configured_repository_status(dir: &Path, marker: &Path) {
    let answer = status(dir).await.unwrap().unwrap();
    assert_eq!(answer.added, 2);
    assert_eq!(answer.deleted, 0);
    assert!(answer
        .changes
        .iter()
        .any(|change| change.path == "tracked.txt" && change.status == " M"));
    assert!(answer
        .changes
        .iter()
        .any(|change| change.path == "staged.txt" && change.status == "A "));
    assert!(
        !marker.exists(),
        "repository Git helpers executed a marker command"
    );
}

async fn seed_unborn_repository_with_git_helper(dir: &Path, marker: &Path) {
    tokio::fs::create_dir_all(dir).await.unwrap();
    let init = Command::new("git")
        .args(["init", "-q"])
        .current_dir(dir)
        .status()
        .await
        .unwrap();
    assert!(init.success());
    let configured = Command::new("git")
        .args([
            "config",
            "core.fsmonitor",
            &format!("touch {}", marker.display()),
        ])
        .current_dir(dir)
        .status()
        .await
        .unwrap();
    assert!(configured.success());
    tokio::fs::write(dir.join("staged.txt"), "staged\n")
        .await
        .unwrap();
    tokio::fs::write(dir.join("untracked.txt"), "untracked\n")
        .await
        .unwrap();
    let add = Command::new("git")
        .args(["-c", "core.fsmonitor=false", "add", "staged.txt"])
        .current_dir(dir)
        .status()
        .await
        .unwrap();
    assert!(add.success());
    drop(tokio::fs::remove_file(marker).await);
}

async fn assert_unborn_repository_status(dir: &Path, marker: &Path) {
    let answer = status(dir).await.unwrap().unwrap();
    assert_eq!(answer.added, 2);
    assert_eq!(answer.deleted, 0);
    assert!(
        !marker.exists(),
        "unborn Git inspection executed a marker command"
    );
}

#[tokio::test]
async fn clean_and_process_filters_cannot_spawn_during_inspection() {
    for helper in ["clean", "process"] {
        for unborn in [false, true] {
            let dir =
                std::env::temp_dir().join(format!("slopd-git-filter-{}", uuid::Uuid::new_v4()));
            std::fs::create_dir_all(&dir).unwrap();
            let git = |args: &[&str]| {
                let output = std::process::Command::new("git")
                    .arg("-C")
                    .arg(&dir)
                    .args(args)
                    .output()
                    .unwrap();
                assert!(
                    output.status.success(),
                    "{}",
                    String::from_utf8_lossy(&output.stderr)
                );
            };
            git(&["init", "-q"]);
            std::fs::write(dir.join("tracked"), "before\n").unwrap();
            git(&["add", "tracked"]);
            if !unborn {
                git(&[
                    "-c",
                    "user.name=test",
                    "-c",
                    "user.email=test@example.invalid",
                    "-c",
                    "commit.gpgsign=false",
                    "commit",
                    "-qm",
                    "initial",
                ]);
                std::fs::write(dir.join("staged"), "staged\n").unwrap();
                git(&["add", "staged"]);
                std::fs::write(dir.join("tracked"), "before\nafter\n").unwrap();
            }
            let marker = dir.join("executed");
            std::fs::write(dir.join(".gitattributes"), "* filter=marker\n").unwrap();
            git(&[
                "config",
                &format!("filter.marker.{helper}"),
                &format!("touch {}; cat", marker.display()),
            ]);
            std::fs::write(dir.join("untracked"), "untracked\n").unwrap();

            let paths = status_with_counts(&dir, false).await.unwrap().unwrap();
            assert!(paths.changes.iter().any(|row| row.path == "tracked"));
            let details = status(&dir).await.unwrap().unwrap();
            assert!(!marker.exists(), "{helper} executed in unborn={unborn}");
            assert_eq!(details.added, if unborn { 3 } else { 4 });
            assert_eq!(details.deleted, 0);
            std::fs::remove_dir_all(&dir).unwrap();
        }
    }
}

#[tokio::test]
async fn a_large_status_is_capped_before_numstat() {
    let dir = std::env::temp_dir().join(format!("slopd-git-large-{}", std::process::id()));
    drop(tokio::fs::remove_dir_all(&dir).await);
    tokio::fs::create_dir_all(&dir).await.unwrap();

    let init = Command::new("git")
        .args(["init", "-q"])
        .current_dir(&dir)
        .status()
        .await
        .unwrap();
    assert!(init.success());

    for n in 0..=LIMIT {
        tokio::fs::write(dir.join(format!("file-{n:04}.txt")), "one\n")
            .await
            .unwrap();
    }

    let answer = status(&dir).await.unwrap().unwrap();
    assert!(answer.truncated);
    assert_eq!(answer.changed, LIMIT);
    assert_eq!(answer.changes.len(), LIMIT);
    assert_eq!(answer.added, 0);
    assert_eq!(answer.deleted, 0);

    tokio::fs::remove_dir_all(&dir).await.unwrap();
}

/// Parse `status --porcelain=v1 -z` without Git's quote escaping. Discard a rename's old path and keep the current path.
fn parse_porcelain(out: &str) -> Vec<(String, String)> {
    let mut rows = Vec::new();
    let mut fields = out.split('\0').filter(|s| !s.is_empty());
    while let Some(record) = fields.next() {
        let Some(((path, status), renamed)) = parse_porcelain_record(record) else {
            continue;
        };
        if renamed {
            // The source path, which nothing here draws.
            fields.next();
        }
        rows.push((path, status));
    }
    rows
}
