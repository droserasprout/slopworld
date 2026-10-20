use super::*;

use crate::test_support::fixture;
use std::path::Path;
fn touch(dir: &Path, name: &str) {
    std::fs::write(dir.join(name), b"").unwrap();
}
fn subdir(dir: &Path, name: &str) {
    std::fs::create_dir_all(dir.join(name)).unwrap();
}
#[tokio::test]
async fn files_are_opt_in() {
    let dir = fixture("optin");
    subdir(&dir, "src");
    subdir(&dir, "empty");
    touch(&dir.join("src"), "lib.rs");
    touch(&dir, "Cargo.toml");
    touch(&dir, "README.md");

    let quiet = list_dir(&dir, false, false, 500, "", false).await.unwrap();
    assert_eq!(quiet.dirs, ["empty", "src"]);
    assert!(quiet.empty_dirs.is_empty());
    assert!(quiet.files.is_empty());

    let full = list_dir(&dir, true, false, 500, "", false).await.unwrap();
    assert_eq!(full.dirs, ["empty", "src"]);
    assert_eq!(full.empty_dirs, ["empty"]);
    assert_eq!(full.files, ["Cargo.toml", "README.md"]);
}

#[tokio::test]
async fn dotfiles_are_hidden_until_they_are_asked_for() {
    let dir = fixture("hidden");
    subdir(&dir, "src");
    subdir(&dir, ".git");
    touch(&dir, "main.rs");
    touch(&dir, ".gitignore");

    let shy = list_dir(&dir, true, false, 500, "", false).await.unwrap();
    assert_eq!(shy.dirs, ["src"]);
    assert_eq!(shy.empty_dirs, ["src"]);
    assert_eq!(shy.files, ["main.rs"]);

    let all = list_dir(&dir, true, true, 500, "", false).await.unwrap();
    assert_eq!(all.dirs, [".git", "src"]);
    assert_eq!(all.empty_dirs, [".git", "src"]);
    assert_eq!(all.files, [".gitignore", "main.rs"]);
}

#[tokio::test]
async fn gitignored_entries_are_classified_before_optional_filtering() {
    let dir = fixture("gitignored");
    subdir(&dir, "ignored-dir");
    subdir(&dir, "visible-dir");
    touch(&dir, "ignored.log");
    touch(&dir, "visible.txt");
    std::fs::write(dir.join(".gitignore"), "ignored.log\nignored-dir\n").unwrap();
    let status = std::process::Command::new("git")
        .current_dir(&*dir)
        .args(["init", "-q"])
        .status()
        .unwrap();
    assert!(status.success());

    let mut visible = list_dir(&dir, true, false, 500, "", false).await.unwrap();
    filter_gitignored(&dir, &mut visible, false).await;
    assert_eq!(visible.gitignored_dirs, ["ignored-dir"]);
    assert_eq!(visible.gitignored_files, ["ignored.log"]);
    assert!(visible.dirs.contains(&"ignored-dir".to_owned()));
    assert!(visible.files.contains(&"ignored.log".to_owned()));

    let mut hidden = list_dir(&dir, true, false, 500, "", false).await.unwrap();
    filter_gitignored(&dir, &mut hidden, true).await;
    assert_eq!(hidden.gitignored_dirs, ["ignored-dir"]);
    assert_eq!(hidden.gitignored_files, ["ignored.log"]);
    assert!(!hidden.dirs.contains(&"ignored-dir".to_owned()));
    assert!(!hidden.files.contains(&"ignored.log".to_owned()));
}

#[tokio::test]
async fn large_gitignore_classification_does_not_deadlock() {
    let dir = fixture("gitignored-large");
    for i in 0..500 {
        touch(&dir, &format!("ignored-{i:03}-{}", "x".repeat(230)));
    }
    std::fs::write(dir.join(".gitignore"), "ignored-*\n").unwrap();
    let status = std::process::Command::new("git")
        .current_dir(&*dir)
        .args(["init", "-q"])
        .status()
        .unwrap();
    assert!(status.success());

    let mut visible = list_dir(&dir, true, false, 500, "", false).await.unwrap();
    tokio::time::timeout(
        std::time::Duration::from_secs(5),
        filter_gitignored(&dir, &mut visible, false),
    )
    .await
    .unwrap();
    assert_eq!(visible.files.len(), 500);
    assert_eq!(visible.gitignored_files.len(), 500);

    let mut hidden = list_dir(&dir, true, false, 500, "", false).await.unwrap();
    tokio::time::timeout(
        std::time::Duration::from_secs(5),
        filter_gitignored(&dir, &mut hidden, true),
    )
    .await
    .unwrap();
    assert!(hidden.files.is_empty());
    assert_eq!(hidden.gitignored_files.len(), 500);
}

#[tokio::test]
async fn gitignore_no_match_keeps_the_listing() {
    let dir = fixture("gitignored-none");
    touch(&dir, "visible.txt");
    std::fs::write(dir.join(".gitignore"), "ignored.log\n").unwrap();
    let status = std::process::Command::new("git")
        .current_dir(&*dir)
        .args(["init", "-q"])
        .status()
        .unwrap();
    assert!(status.success());

    let mut listing = list_dir(&dir, true, false, 500, "", false).await.unwrap();
    filter_gitignored(&dir, &mut listing, true).await;
    assert_eq!(listing.files, ["visible.txt"]);
    assert!(listing.gitignored_files.is_empty());
}

/// `DirEntry::file_type` does not follow a symlink. Therefore, without the stat behind it a linked
/// directory is in neither list and the tree draws it as gone. A link to nowhere stays in neither,
/// which is the one case where that is the right answer.
#[cfg(unix)]
#[tokio::test]
async fn a_symlinked_directory_is_a_directory() {
    let dir = fixture("links");
    subdir(&dir, "real");
    touch(&dir, "file.txt");
    std::os::unix::fs::symlink(dir.join("real"), dir.join("to-dir")).unwrap();
    std::os::unix::fs::symlink(dir.join("file.txt"), dir.join("to-file")).unwrap();
    std::os::unix::fs::symlink(dir.join("nowhere"), dir.join("dangling")).unwrap();

    let out = list_dir(&dir, true, false, 500, "", false).await.unwrap();
    assert_eq!(out.dirs, ["real", "to-dir"]);
    assert_eq!(out.empty_dirs, ["real", "to-dir"]);
    assert_eq!(out.files, ["file.txt", "to-file"]);
}

/// Report when the read limit truncates a directory.
/// Apply the limit to entries before sorting them.

#[tokio::test]
async fn a_long_directory_is_cut_short_and_says_so() {
    let dir = fixture("cap");
    for i in 0..20 {
        touch(&dir, &format!("f{i:02}"));
    }

    let capped = list_dir(&dir, true, false, 5, "", false).await.unwrap();
    assert_eq!(capped.dirs.len() + capped.files.len(), 5);
    assert!(capped.truncated);

    let whole = list_dir(&dir, true, false, 500, "", false).await.unwrap();
    assert_eq!(whole.files.len(), 20);
    assert!(!whole.truncated);
}

#[tokio::test]
async fn exactly_the_limit_is_not_truncated() {
    let dir = fixture("exact-cap");
    for i in 0..5 {
        touch(&dir, &format!("f{i:02}"));
    }

    let exact = list_dir(&dir, true, false, 5, "", false).await.unwrap();
    assert_eq!(exact.files.len(), 5);
    assert!(!exact.truncated);

    let dirs = fixture("exact-dir-cap");
    for i in 0..5 {
        subdir(&dirs, &format!("d{i:02}"));
    }
    touch(&dirs, "ignored-file");
    let exact_dirs = list_dir(&dirs, false, false, 5, "", false).await.unwrap();
    assert_eq!(exact_dirs.dirs.len(), 5);
    assert!(!exact_dirs.truncated);
}

#[test]
fn browse_limit_is_bounded() {
    assert_eq!(browse_limit(None), 500);
    assert_eq!(browse_limit(Some(0)), 1);
    assert_eq!(browse_limit(Some(12)), 12);
    assert_eq!(browse_limit(Some(usize::MAX)), 500);
}

#[tokio::test]
async fn browse_filter_reaches_children_omitted_by_the_limit() {
    let dir = fixture("filtered-cap");
    for i in 0..8 {
        subdir(&dir, &format!("child-{i}"));
    }
    let capped = list_dir(&dir, false, false, 2, "", false).await.unwrap();
    assert!(capped.truncated);
    let omitted = (0..8)
        .map(|i| format!("child-{i}"))
        .find(|name| !capped.dirs.contains(name))
        .unwrap();
    let found = list_dir(&dir, false, false, 2, &omitted, false)
        .await
        .unwrap();
    assert_eq!(found.dirs, [omitted]);
    assert!(!found.truncated);
    let absent = list_dir(&dir, false, false, 2, "missing", false)
        .await
        .unwrap();
    assert!(absent.dirs.is_empty());
    assert!(!absent.truncated);
}
