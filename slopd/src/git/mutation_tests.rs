//! Exercise mutation policy against real indexes and working files.
use super::*;
use crate::shared::wire::Empty;
use std::fs;

fn git(dir: &Path, args: &[&str]) -> String {
    let output = std::process::Command::new("git")
        .arg("-C")
        .arg(dir)
        .args(args)
        .output()
        .unwrap();
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
    String::from_utf8(output.stdout).unwrap()
}

fn file(path: &str, status: &str) -> GitSelection {
    GitSelection {
        path: path.into(),
        status: Some(status.into()),
    }
}

#[tokio::test]
async fn mutations_preserve_selected_paths_and_recheck_discard_state() {
    let fixture = crate::test_support::fixture("mutation");
    let dir: &Path = &fixture;
    let root = dir.to_str().unwrap();
    git(dir, &["init", "-q"]);
    git(dir, &["config", "user.name", "Test"]);
    git(dir, &["config", "user.email", "test@example.invalid"]);
    git(dir, &["config", "commit.gpgSign", "false"]);
    let name = ":(glob)*.txt";
    fs::write(dir.join(name), "original").unwrap();
    fs::write(dir.join("other.txt"), "other").unwrap();
    execute(dir, root, Operation::Stage(file(name, "??")))
        .await
        .unwrap();
    execute(dir, root, Operation::Discard(file(name, "??")))
        .await
        .unwrap_err();
    assert_eq!(git(dir, &["ls-files"]).trim(), name);
    execute(dir, root, Operation::UnstageAll(Empty {}))
        .await
        .unwrap();
    assert!(git(dir, &["ls-files"]).is_empty());
    execute(dir, root, Operation::StageAll(Empty {}))
        .await
        .unwrap();
    execute(dir, root, Operation::Commit("initial".into()))
        .await
        .unwrap();
    fs::write(dir.join(name), "modified").unwrap();
    execute(dir, root, Operation::Discard(file(name, " M")))
        .await
        .unwrap();
    assert_eq!(fs::read(dir.join(name)).unwrap(), b"original");
    fs::create_dir(dir.join("dst")).unwrap();
    git(dir, &["--literal-pathspecs", "mv", name, "dst/new"]);
    execute(
        dir,
        root,
        Operation::Unstage(GitSelection {
            path: "dst".into(),
            status: None,
        }),
    )
    .await
    .unwrap();
    assert!(git(dir, &["diff", "--cached", "--name-only"]).is_empty());
    std::os::unix::fs::symlink("dst", dir.join("link")).unwrap();
    execute(dir, root, Operation::Discard(file("link", "??")))
        .await
        .unwrap();
    assert!(!dir.join("link").is_symlink());
    assert!(dir.join("dst/new").exists());
}
