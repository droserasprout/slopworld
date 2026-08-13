//! Host-side git snapshot for the game, which cannot enter agent mount namespaces.
//! Collects repository root, porcelain status, and numstat/shortstat for the tree.

use std::collections::HashMap;
use std::path::{Path, PathBuf};

use tokio::process::Command;

/// One changed path.
pub struct Change {
    /// Relative to the repository root, which is what the tree is drawn from and what
    /// `git diff` takes back.
    pub path: String,
    /// The porcelain pair, staged letter then unstaged: `M `, ` M`, `??`, `R `. Kept as the
    /// two characters git wrote rather than reduced to one, because "staged" and "modified
    /// since" are different rows to a reader and the same letter otherwise.
    pub status: String,
    /// Lines, from the numstat. `None` where git said `-` (a binary file) or where there is
    /// no diff to count - an untracked file has no blob to compare against.
    pub added: Option<u32>,
    pub deleted: Option<u32>,
}

/// A repository as this endpoint sees it.
pub struct Status {
    pub root: PathBuf,
    /// The branch, or the short hash on a detached head, or empty on a repository with no
    /// commit yet.
    pub branch: String,
    pub changed: usize,
    pub added: u32,
    pub deleted: u32,
    pub truncated: bool,
    pub changes: Vec<Change>,
}

/// Enough for any working tree a person is actually reading. Past it the tree is not a tree
/// any more, and a `git status` that long is a build directory somebody forgot to ignore.
const LIMIT: usize = 2000;

/// Whether `dir` is inside a repository, and what has changed in it. `Ok(None)` is "not a
/// repository" - a plain answer rather than an error, because half the projects on a machine
/// are not one and the view says so in a line.
pub async fn status(dir: &Path) -> std::io::Result<Option<Status>> {
    let Some(root) = toplevel(dir).await? else {
        return Ok(None);
    };

    // Both readings are against the root rather than the directory asked about: a project
    // pointed at a subdirectory of a repository is still that repository, and a path relative
    // to the root is the one form `git diff` will take back without a second guess about cwd.
    let branch = branch(&root).await;
    let counts = numstat(&root).await;

    // Ask for every untracked file rather than Git's default one-row-per-directory
    // summary. The latter produces `?? path/`, which is not a file path and would
    // become an empty child when the client folds the list into its tree.
    let porcelain = run(
        &root,
        &["status", "--porcelain=v1", "--untracked-files=all", "-z"],
    )
    .await?;
    let rows = parse_porcelain(&porcelain);
    let changed = rows.len();
    let added = counts.values().filter_map(|c| c.0).sum();
    let deleted = counts.values().filter_map(|c| c.1).sum();
    let mut changes = Vec::new();
    for (path, status) in rows.into_iter().take(LIMIT) {
        let (added, deleted) = counts.get(&path).copied().unwrap_or((None, None));
        changes.push(Change {
            path,
            status,
            added,
            deleted,
        });
    }

    changes.sort_by(|a, b| a.path.cmp(&b.path));
    Ok(Some(Status {
        root,
        branch,
        changed,
        added,
        deleted,
        truncated: changed > LIMIT,
        changes,
    }))
}

/// The repository root, or `None` where there is none. An error here is git missing or
/// unrunnable, which is worth saying; a non-zero exit is only "not a repository".
async fn toplevel(dir: &Path) -> std::io::Result<Option<PathBuf>> {
    let out = Command::new("git")
        .arg("-C")
        .arg(dir)
        .args(["rev-parse", "--show-toplevel"])
        .output()
        .await?;
    if !out.status.success() {
        return Ok(None);
    }
    let path = String::from_utf8_lossy(&out.stdout).trim().to_string();
    Ok((!path.is_empty()).then(|| PathBuf::from(path)))
}

/// The branch name, the short hash when the head is detached, and empty when neither answers -
/// a repository with nothing committed yet has a head pointing at a branch that does not exist.
async fn branch(root: &Path) -> String {
    if let Ok(out) = run(root, &["symbolic-ref", "--short", "-q", "HEAD"]).await {
        let name = out.trim().to_string();
        if !name.is_empty() {
            return name;
        }
    }
    run(root, &["rev-parse", "--short", "HEAD"])
        .await
        .map(|s| s.trim().to_string())
        .unwrap_or_default()
}

/// Lines added and deleted per path, staged and unstaged together - `HEAD` is the comparison
/// a reader means by "what has changed here". A repository with no commit has no `HEAD` to
/// compare against, and the fallback is the index, which is all such a repository can show.
async fn numstat(root: &Path) -> HashMap<String, (Option<u32>, Option<u32>)> {
    let out = match run(root, &["diff", "--numstat", "-z", "HEAD"]).await {
        Ok(s) => s,
        Err(_) => run(root, &["diff", "--numstat", "-z", "--cached"])
            .await
            .unwrap_or_default(),
    };
    parse_numstat(&out)
}

/// `git` in the repository, with its own environment kept out of the way: a pager here would
/// wait for a terminal nobody has, and a locale would translate the words this parses.
async fn run(root: &Path, args: &[&str]) -> std::io::Result<String> {
    let out = Command::new("git")
        .arg("-C")
        .arg(root)
        .args(args)
        .env("GIT_PAGER", "cat")
        .env("LC_ALL", "C")
        .output()
        .await?;
    if !out.status.success() {
        return Err(std::io::Error::other(
            String::from_utf8_lossy(&out.stderr).trim().to_string(),
        ));
    }
    Ok(String::from_utf8_lossy(&out.stdout).into_owned())
}

/// `status --porcelain=v1 -z`: two status characters, a space, then the path, NUL-terminated.
/// A rename is that record followed by a *second* NUL-terminated field, the path it came from.
/// `-z` rather than the quoted-and-escaped default, where a path with a newline or a quote in
/// it is unparseable without undoing git's own escaping first. The old name of a rename is
/// dropped: the row is about where the file is now.
fn parse_porcelain(out: &str) -> Vec<(String, String)> {
    let mut rows = Vec::new();
    let mut fields = out.split('\0').filter(|s| !s.is_empty());
    while let Some(record) = fields.next() {
        if record.len() < 4 {
            continue;
        }
        let (status, path) = record.split_at(2);
        let renamed = status.starts_with('R') || status.starts_with('C');
        if renamed {
            // The source path, which nothing here draws.
            fields.next();
        }
        // The first space is porcelain's separator. Do not trim the rest: a perfectly
        // legitimate filename may itself begin with a space.
        let path = path.strip_prefix(' ').unwrap_or(path);
        rows.push((path.to_string(), status.to_string()));
    }
    rows
}

/// `diff --numstat -z`: added, deleted and the path, tab-separated, and the path its own
/// NUL-terminated field rather than part of the line. A rename splits into two more fields
/// (from, then to) after the counts, and the second is the one a row is about.
fn parse_numstat(out: &str) -> HashMap<String, (Option<u32>, Option<u32>)> {
    let mut map = HashMap::new();
    let mut fields = out.split('\0').filter(|s| !s.is_empty());
    while let Some(head) = fields.next() {
        let mut parts = head.split('\t');
        let (Some(added), Some(deleted)) = (parts.next(), parts.next()) else {
            continue;
        };
        // `-` where git would not count: a binary file has no lines to add.
        let n = |s: &str| s.parse::<u32>().ok();
        let path = match parts.next() {
            // The path rode on the same field, which it does for everything but a rename.
            Some(p) if !p.is_empty() => p.to_string(),
            // A rename: the old name, then the new one, each its own field.
            _ => match (fields.next(), fields.next()) {
                (Some(_), Some(to)) => to.to_string(),
                _ => continue,
            },
        };
        map.insert(path, (n(added), n(deleted)));
    }
    map
}

#[cfg(test)]
mod tests {
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

    #[tokio::test]
    async fn a_directory_that_is_no_repository_is_not_an_error() {
        let dir = std::env::temp_dir().join("slopd-git-none");
        let _ = tokio::fs::create_dir_all(&dir).await;
        // Only meaningful where the temp dir is not itself inside a checkout, which is the
        // usual arrangement; a machine where it is would make this vacuous rather than wrong.
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

        let init = Command::new("git")
            .arg("init")
            .arg("-q")
            .current_dir(&dir)
            .status()
            .await
            .unwrap();
        assert!(init.success());

        let answer = status(&dir).await.unwrap().unwrap();
        assert_eq!(answer.changes.len(), 1);
        assert_eq!(answer.changes[0].path, "untracked/nested/file.txt");
        assert_eq!(answer.changes[0].status, "??");

        tokio::fs::remove_dir_all(&dir).await.unwrap();
    }
}
