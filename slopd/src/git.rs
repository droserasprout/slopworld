//! Host-side git snapshot for the game, which cannot enter agent mount namespaces.
//! Collects repository root, porcelain status, and path-limited numstat for the tree.

use std::collections::{HashMap, HashSet};
use std::path::{Path, PathBuf};
use std::process::Stdio;

#[path = "git_exec.rs"]
mod git_exec;

use futures::stream::{self, StreamExt};
use tokio::io::{AsyncBufReadExt, AsyncReadExt, BufReader};
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
    /// Lines, from the numstat. `None` where git said `-` (a binary file).
    pub added: Option<u32>,
    pub deleted: Option<u32>,
}

/// A repository as this endpoint sees it.
pub struct Status {
    pub root: PathBuf,
    /// The branch, or the short hash on a detached head, or empty on a repository with no
    /// commit yet.
    pub branch: String,
    /// Number of rows returned. When `truncated` is true this is a lower bound.
    pub changed: usize,
    pub added: u32,
    pub deleted: u32,
    pub truncated: bool,
    pub counts_complete: bool,
    pub changes: Vec<Change>,
}

/// A status row before it is exposed to the client. Git emits a rename's old path in a
/// second NUL-delimited field; it is not drawn, but numstat needs it as a pathspec for Git to
/// recognize the rename instead of treating the new path as a full-file addition.
struct StatusRow {
    path: String,
    status: String,
    source: Option<String>,
}

/// Enough for any working tree a person is actually reading. Past it the tree is not a tree
/// any more, and a `git status` that long is a build directory somebody forgot to ignore.
const LIMIT: usize = 2000;
const NO_INDEX_CONCURRENCY: usize = 16;

/// Disable known helpers; seccomp also blocks filters and other child processes.
pub(crate) fn inspection_args() -> &'static [&'static str] {
    &[
        "-c",
        "core.fsmonitor=false",
        "-c",
        "diff.external=",
        "-c",
        "core.hooksPath=/dev/null",
    ]
}

pub(crate) fn inspection_command(root: &Path) -> Command {
    let mut command = Command::from(inspection_std_command(root));
    command.kill_on_drop(true);
    command
}

pub(crate) fn inspection_std_command(root: &Path) -> std::process::Command {
    let mut command = std::process::Command::new("git");
    command.args(inspection_args()).arg("-C").arg(root);
    command
        .env("GIT_PAGER", "cat")
        .env("GIT_TERMINAL_PROMPT", "0")
        .env("LC_ALL", "C");
    git_exec::restrict(&mut command);
    command
}

/// Warm the untracked-directory cache before the first sidebar read.
pub async fn warm_projects(dirs: Vec<PathBuf>) {
    stream::iter(dirs)
        .for_each_concurrent(4, |dir| async move {
            if let Err(error) = warm(&dir).await {
                tracing::debug!(path = %dir.display(), error = %error, "Git cache warmup skipped");
            }
        })
        .await;
}

async fn warm(dir: &Path) -> std::io::Result<()> {
    let Some(root) = toplevel(dir).await? else {
        return Ok(());
    };
    let _ = status_rows(&root).await?;
    Ok(())
}

/// Whether `dir` is inside a repository, and what has changed in it. `Ok(None)` is "not a
/// repository" - a plain answer rather than an error, because half the projects on a machine
/// are not one and the view says so in a line.
#[cfg(test)]
pub async fn status(dir: &Path) -> std::io::Result<Option<Status>> {
    status_with_counts(dir, true).await
}

/// Status-only reads let the sidebar display paths before computing line counts.
pub async fn status_with_counts(
    dir: &Path,
    include_counts: bool,
) -> std::io::Result<Option<Status>> {
    let _perf = crate::perf::timer("git-refresh");
    let Some(root) = toplevel(dir).await? else {
        return Ok(None);
    };

    // Both readings are against the root rather than the directory asked about: a project
    // pointed at a subdirectory of a repository is still that repository, and a path relative
    // to the root is the one form `git diff` will take back without a second guess about cwd.
    // Start with Git's one-row-per-directory untracked summary. It can skip ignored trees, which
    // matters for projects that contain build outputs or nested checkouts. Ordinary untracked
    // directories are expanded in one batch below; a nested repository remains a boundary row
    // and its own working tree is never inspected as part of the parent.
    let (rows_result, branch) = tokio::join!(status_rows(&root), branch(&root));
    let (rows, truncated) = rows_result?;
    crate::perf::count("git-status-rows", rows.len() as u64);
    // A truncated status has no complete count to report. More importantly, running a full
    // diff or one no-index diff per untracked path here would undo the status stream's cap.
    // Counts are optional decoration. Bound the whole pass, including per-file child
    // processes, so a huge file or slow diff cannot hold the request indefinitely.
    let counts = if truncated || !include_counts {
        None
    } else {
        tokio::time::timeout(std::time::Duration::from_secs(2), numstat(&root, &rows))
            .await
            .ok()
    };
    let counts_complete = counts.is_some();
    let counts = counts.unwrap_or_default();
    let changed = rows.len();
    let added = counts.values().filter_map(|c| c.0).sum();
    let deleted = counts.values().filter_map(|c| c.1).sum();
    let mut changes = Vec::new();
    for row in rows.into_iter().take(LIMIT) {
        let (added, deleted) = counts.get(&row.path).copied().unwrap_or((None, None));
        changes.push(Change {
            path: row.path,
            status: row.status,
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
        truncated,
        counts_complete,
        changes,
    }))
}

/// Read the status stream only far enough to fill the sidebar tree. `status -z` puts the old
/// name of a rename/copy in a second NUL-delimited field, so consume that field before deciding
/// whether the next row crossed the cap.
async fn status_rows(root: &Path) -> std::io::Result<(Vec<StatusRow>, bool)> {
    let _perf = crate::perf::timer("git-status");
    let (mut rows, truncated) = status_rows_command(root, &[], "normal").await?;
    if truncated {
        return Ok((rows, true));
    }

    // Git's normal summary is deliberately cheap, but the sidebar still wants file rows for a
    // small ordinary untracked directory. Ask Git to expand only those rows. Git itself reports
    // a nested repository as `?? nested/` even with `--untracked-files=all`, so this does not walk
    // into a child checkout.
    let directories: HashSet<String> = rows
        .iter()
        .filter(|row| row.status == "??" && row.path.ends_with('/'))
        .map(|row| row.path.clone())
        .collect();
    if !directories.is_empty() {
        let paths: Vec<String> = directories.iter().cloned().collect();
        let (expanded, expanded_truncated) = status_rows_command(root, &paths, "all").await?;
        rows.retain(|row| !(row.status == "??" && directories.contains(&row.path)));
        rows.extend(expanded);
        if rows.len() > LIMIT {
            rows.truncate(LIMIT);
            return Ok((rows, true));
        }
        if expanded_truncated {
            return Ok((rows, true));
        }
    }
    Ok((rows, false))
}

/// Read one bounded Git status stream. `paths` are repository-relative pathspecs used when the
/// fast summary found ordinary untracked directories worth expanding.
async fn status_rows_command(
    root: &Path,
    paths: &[String],
    untracked_files: &str,
) -> std::io::Result<(Vec<StatusRow>, bool)> {
    let mut command = inspection_command(root);
    command
        // Persist directory-mtime caches without enabling repository fsmonitor commands.
        .args(["-c", "core.untrackedCache=true"])
        .args(["status", "--porcelain=v1"])
        .arg(format!("--untracked-files={untracked_files}"))
        .args(["--ignore-submodules=all", "-z"]);
    if !paths.is_empty() {
        command.arg("--");
        command.args(paths);
    }
    let mut child = command
        // Let Git persist its untracked-directory cache in the index. The first scan of a large
        // worktree can still be cold, but without this cache every daemon restart pays that
        // directory walk again; later sidebar refreshes are then only metadata checks.
        .env("GIT_OPTIONAL_LOCKS", "1")
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .kill_on_drop(true)
        .spawn()?;
    let stdout = child
        .stdout
        .take()
        .ok_or_else(|| std::io::Error::other("capturing git status output"))?;
    let mut stdout = BufReader::new(stdout);
    let stderr = child
        .stderr
        .take()
        .ok_or_else(|| std::io::Error::other("capturing git status errors"))?;
    let stderr_task = tokio::spawn(async move {
        let mut error = String::new();
        let _ = BufReader::new(stderr).read_to_string(&mut error).await;
        error
    });
    let mut fields = Vec::new();
    let mut rows = Vec::with_capacity(LIMIT);

    loop {
        fields.clear();
        if stdout.read_until(0, &mut fields).await? == 0 {
            break;
        }
        if fields.last() == Some(&0) {
            fields.pop();
        }
        let record = String::from_utf8_lossy(&fields);
        let Some(((path, status), renamed)) = parse_porcelain_record(&record) else {
            continue;
        };
        let source = if renamed {
            // The source path is not drawn, but it is part of this status record and is
            // required as a pathspec for numstat to retain rename detection.
            fields.clear();
            if stdout.read_until(0, &mut fields).await? == 0 {
                None
            } else {
                if fields.last() == Some(&0) {
                    fields.pop();
                }
                Some(String::from_utf8_lossy(&fields).into_owned())
            }
        } else {
            None
        };
        rows.push(StatusRow {
            path,
            status,
            source,
        });
        if rows.len() > LIMIT {
            let _ = child.kill().await;
            let _ = child.wait().await;
            let _ = stderr_task.await;
            rows.truncate(LIMIT);
            return Ok((rows, true));
        }
    }

    let status = child.wait().await?;
    let error = stderr_task.await.unwrap_or_default();
    if !status.success() {
        let error = error.trim();
        return Err(std::io::Error::other(if error.is_empty() {
            "git status failed".to_string()
        } else {
            error.to_string()
        }));
    }
    Ok((rows, false))
}

/// The repository root, or `None` where there is none. An error here is git missing or
/// unrunnable, which is worth saying; a non-zero exit is only "not a repository".
async fn toplevel(dir: &Path) -> std::io::Result<Option<PathBuf>> {
    let out = inspection_command(dir)
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
/// compare against, and the fallback is the index. Untracked paths have no blob for that
/// comparison, so each gets the same no-index reading used by the diff pager. In an unborn
/// repository, staged additions also need the no-index reading so later worktree edits are not
/// lost behind the cached version.
async fn numstat(root: &Path, rows: &[StatusRow]) -> HashMap<String, (Option<u32>, Option<u32>)> {
    let _perf = crate::perf::timer("git-numstat");
    if rows.is_empty() {
        return HashMap::new();
    }

    let mut tracked = Vec::new();
    for row in rows.iter().filter(|row| row.status != "??") {
        tracked.push(row.path.as_str());
        if let Some(source) = row.source.as_deref() {
            tracked.push(source);
        }
    }
    let (out, has_head) = if tracked.is_empty() {
        // An untracked-only answer has no index-side diff to read. Its paths are counted by the
        // no-index pass below, and skipping this command matters when a directory expands to
        // many files.
        (String::new(), true)
    } else {
        match run_paths(
            root,
            &[
                "diff",
                "--no-ext-diff",
                "--no-textconv",
                "--find-renames",
                "--numstat",
                "-z",
                "HEAD",
            ],
            tracked.iter().copied(),
        )
        .await
        {
            Ok(s) => (s, true),
            Err(_) => (
                run_paths(
                    root,
                    &[
                        "diff",
                        "--no-ext-diff",
                        "--no-textconv",
                        "--find-renames",
                        "--numstat",
                        "-z",
                        "--cached",
                    ],
                    tracked.iter().copied(),
                )
                .await
                .unwrap_or_default(),
                false,
            ),
        }
    };
    let mut counts = parse_numstat(&out);

    let untracked = rows
        .iter()
        .filter_map(|row| {
            (row.status == "??" || (!has_head && row.status.contains('A'))).then_some(&row.path)
        })
        .filter(|path| !path.ends_with('/') && !is_directory(root, path))
        .cloned()
        .collect::<Vec<_>>();
    crate::perf::count("git-no-index-files", untracked.len() as u64);
    let no_index = stream::iter(untracked.into_iter().map(|path| async move {
        let count = no_index_numstat(root, &path).await;
        (path, count)
    }))
    .buffer_unordered(NO_INDEX_CONCURRENCY)
    .collect::<Vec<_>>()
    .await;
    for (path, count) in no_index {
        if let Some(count) = count {
            counts.insert(path, count);
        }
    }

    counts
}

/// Git uses a trailing slash for an untracked directory, but a gitlink added to the index is
/// reported without one. Never hand either kind of directory to `diff --no-index`: it would
/// walk a nested checkout that the status pass intentionally treats as one boundary row.
fn is_directory(root: &Path, path: &str) -> bool {
    std::fs::symlink_metadata(root.join(path))
        .map(|metadata| metadata.file_type().is_dir())
        .unwrap_or(false)
}

/// `git diff --no-index` exits one when the file differs from `/dev/null`, but its numstat is
/// still the authoritative text/binary classification and line count. A failure to read the
/// file leaves it absent from the map, just as an unavailable tracked diff does.
async fn no_index_numstat(root: &Path, path: &str) -> Option<(Option<u32>, Option<u32>)> {
    let out = inspection_command(root)
        .args([
            "diff",
            "--no-ext-diff",
            "--no-textconv",
            "--no-index",
            "--numstat",
            "-z",
            "--",
            "/dev/null",
        ])
        .arg(path)
        .env("GIT_OPTIONAL_LOCKS", "0")
        .output()
        .await
        .ok()?;

    if out.status.success() || out.status.code() == Some(1) {
        return parse_numstat(&String::from_utf8_lossy(&out.stdout))
            .into_iter()
            .next()
            .map(|(_, count)| count);
    }

    None
}

/// `git` in the repository, with its own environment kept out of the way: a pager here would
/// wait for a terminal nobody has, and a locale would translate the words this parses.
async fn run(root: &Path, args: &[&str]) -> std::io::Result<String> {
    let out = inspection_command(root)
        .args(args)
        .env("GIT_OPTIONAL_LOCKS", "0")
        .output()
        .await?;
    if !out.status.success() {
        return Err(std::io::Error::other(
            String::from_utf8_lossy(&out.stderr).trim().to_string(),
        ));
    }
    Ok(String::from_utf8_lossy(&out.stdout).into_owned())
}

/// Run a diff against only the paths the status stream returned. Without the pathspec, a
/// repository with a large number of tracked edits would make the numstat pass scan the whole
/// diff even though the sidebar can display only `LIMIT` rows.
async fn run_paths<'a, I>(root: &Path, args: &[&str], paths: I) -> std::io::Result<String>
where
    I: IntoIterator<Item = &'a str>,
{
    let mut command = inspection_command(root);
    command.args(args).arg("--");
    for path in paths {
        command.arg(path);
    }
    let out = command.env("GIT_OPTIONAL_LOCKS", "0").output().await?;
    if !out.status.success() {
        return Err(std::io::Error::other(
            String::from_utf8_lossy(&out.stderr).trim().to_string(),
        ));
    }
    Ok(String::from_utf8_lossy(&out.stdout).into_owned())
}

/// Parse `status --porcelain=v1 -z` without Git's quote escaping; discard a rename's old path and keep the current path.
#[cfg(test)]
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

/// Parse one status field and say whether it consumes the following source-path field.
fn parse_porcelain_record(record: &str) -> Option<((String, String), bool)> {
    if record.len() < 4 {
        return None;
    }
    let (status, path) = record.split_at(2);
    let renamed = status.starts_with('R') || status.starts_with('C');
    // The first space is porcelain's separator. Do not trim the rest: a perfectly legitimate
    // filename may itself begin with a space.
    let path = path.strip_prefix(' ').unwrap_or(path);
    Some(((path.to_string(), status.to_string()), renamed))
}

/// `diff --numstat -z`: added, deleted and the path, tab-separated, and the path its own
/// NUL-terminated field rather than part of the line. A rename splits into two more fields
/// (from, then to) after the counts, and the second is the one a row is about.
fn parse_numstat(out: &str) -> HashMap<String, (Option<u32>, Option<u32>)> {
    let mut map = HashMap::new();
    let mut fields = out.split('\0').filter(|s| !s.is_empty());
    while let Some(head) = fields.next() {
        // The counts occupy the first two tab-separated fields; keep the rest intact because
        // Git permits a tab in a filename. Renames use an empty third field and carry both
        // names as their following NUL-separated fields.
        let mut parts = head.splitn(3, '\t');
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

    #[test]
    fn numstat_keeps_tabs_in_a_filename() {
        let map = parse_numstat("2\t0\ttab\tname.txt\0");
        assert_eq!(map["tab\tname.txt"], (Some(2), Some(0)));
    }

    #[tokio::test]
    async fn a_pure_rename_keeps_zero_line_counts() {
        let dir = std::env::temp_dir().join(format!("slopd-git-rename-{}", std::process::id()));
        let _ = tokio::fs::remove_dir_all(&dir).await;
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
        let _ = tokio::fs::remove_dir_all(&dir).await;
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
        let _ = tokio::fs::remove_dir_all(&dir).await;
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
        tokio::fs::create_dir_all(&dir).await.unwrap();

        let init = Command::new("git")
            .args(["init", "-q"])
            .current_dir(&dir)
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
                .current_dir(&dir)
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
            .current_dir(&dir)
            .status()
            .await
            .unwrap();
        assert!(add.success());
        let commit = Command::new("git")
            .args(["-c", "commit.gpgsign=false", "commit", "-qm", "initial"])
            .current_dir(&dir)
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
                .current_dir(&dir)
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
            .current_dir(&dir)
            .status()
            .await
            .unwrap();
        assert!(add.success());
        let _ = tokio::fs::remove_file(&marker).await;

        let answer = status(&dir).await.unwrap().unwrap();
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

        let unborn = dir.join("unborn");
        tokio::fs::create_dir_all(&unborn).await.unwrap();
        let init = Command::new("git")
            .args(["init", "-q"])
            .current_dir(&unborn)
            .status()
            .await
            .unwrap();
        assert!(init.success());
        let unborn_marker = unborn.with_extension("marker");
        let configured = Command::new("git")
            .args([
                "config",
                "core.fsmonitor",
                &format!("touch {}", unborn_marker.display()),
            ])
            .current_dir(&unborn)
            .status()
            .await
            .unwrap();
        assert!(configured.success());
        tokio::fs::write(unborn.join("staged.txt"), "staged\n")
            .await
            .unwrap();
        tokio::fs::write(unborn.join("untracked.txt"), "untracked\n")
            .await
            .unwrap();
        let add = Command::new("git")
            .args(["-c", "core.fsmonitor=false", "add", "staged.txt"])
            .current_dir(&unborn)
            .status()
            .await
            .unwrap();
        assert!(add.success());
        let _ = tokio::fs::remove_file(&unborn_marker).await;

        let unborn_answer = status(&unborn).await.unwrap().unwrap();
        assert_eq!(unborn_answer.added, 2);
        assert_eq!(unborn_answer.deleted, 0);
        assert!(
            !unborn_marker.exists(),
            "unborn Git inspection executed a marker command"
        );

        tokio::fs::remove_dir_all(&dir).await.unwrap();
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
        let _ = tokio::fs::remove_dir_all(&dir).await;
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
}
