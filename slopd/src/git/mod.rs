//! Host-side git snapshot for the game, which cannot enter agent mount namespaces.
//! Collects repository root, porcelain status, and path-limited numstat for the tree.

use std::collections::{HashMap, HashSet};
use std::path::{Path, PathBuf};
use std::process::Stdio;

mod exec;

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
/// second NUL-delimited field. It is not drawn, but numstat needs it as a pathspec for Git to
/// recognize the rename instead of treating the new path as a full-file addition.
struct StatusRow {
    path: String,
    status: String,
    source: Option<String>,
}

/// Counts may be partial when any tracked or per-file Git diff fails.
#[derive(Default)]
struct Counts {
    by_path: HashMap<String, (Option<u32>, Option<u32>)>,
    complete: bool,
}

/// Enough for any working tree a person is actually reading. Past it the tree is not a tree
/// any more, and a `git status` that long is a build directory somebody forgot to ignore.
const LIMIT: usize = 2000;
const NO_INDEX_CONCURRENCY: usize = 4;
// The sidebar asks about every visible project at once. Share the expensive count budget
// across repositories while leaving status-only reads free to deliver paths immediately.
static NUMSTAT_SLOTS: tokio::sync::Semaphore = tokio::sync::Semaphore::const_new(2);

/// Disable known helpers. Seccomp also blocks filters and other child processes.
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
    exec::restrict(&mut command);
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
    drop(status_rows(&root).await?);
    Ok(())
}

/// Return changes in the repository that contains `dir`.
/// Return `Ok(None)` when `dir` is not in a repository because this is a normal project state.
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

    // Read status and diffs relative to the repository root.
    // A project can select a subdirectory, but Git still requires root-relative diff paths.
    // Start with Git's one-row-per-directory summary for untracked content.
    // It can omit ignored trees, including build output and nested checkouts.
    // Expand ordinary untracked directories in one batch below.
    // Keep nested repositories as boundary rows.
    // own working tree is never inspected as part of the parent.
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
    let counts_complete = counts.as_ref().is_some_and(|counts| counts.complete);
    let counts = counts.unwrap_or_default();
    let changed = rows.len();
    let added = counts.by_path.values().filter_map(|c| c.0).sum();
    let deleted = counts.by_path.values().filter_map(|c| c.1).sum();
    let mut changes = Vec::new();
    for row in rows.into_iter().take(LIMIT) {
        let (added, deleted) = counts
            .by_path
            .get(&row.path)
            .copied()
            .unwrap_or((None, None));
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

/// Read the status stream only far enough to fill the sidebar tree. `status -z` puts the old name
/// of a rename/copy in a second NUL-delimited field. Therefore, consume that field before deciding
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
        // directory walk again. Later sidebar refreshes are then only metadata checks.
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
        drop(BufReader::new(stderr).read_to_string(&mut error).await);
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
            // The source path is not drawn. However, It is part of this status record and is
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
            drop(child.kill().await);
            drop(child.wait().await);
            drop(stderr_task.await);
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

/// The repository root, or `None` outside a repository. Other Git failures are errors.
async fn toplevel(dir: &Path) -> std::io::Result<Option<PathBuf>> {
    let out = inspection_command(dir)
        .args(["rev-parse", "--show-toplevel"])
        .output()
        .await?;
    if !out.status.success() {
        let error = String::from_utf8_lossy(&out.stderr);
        if error.starts_with("fatal: not a git repository (") {
            return Ok(None);
        }
        return Err(std::io::Error::other(error.trim().to_string()));
    }
    let path = String::from_utf8_lossy(&out.stdout);
    let path = path.strip_suffix('\n').unwrap_or(&path);
    let path = path.strip_suffix('\r').unwrap_or(path);
    Ok((!path.is_empty()).then(|| PathBuf::from(path)))
}

/// Return the branch name or the short hash for a detached head.
/// Return an empty string for a new repository whose branch does not exist yet.
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
#[expect(
    clippy::expect_used,
    reason = "the process-global numstat semaphore is never closed"
)]
async fn numstat(root: &Path, rows: &[StatusRow]) -> Counts {
    let _perf = crate::perf::timer("git-numstat");
    if rows.is_empty() {
        return Counts {
            complete: true,
            ..Counts::default()
        };
    }

    // Waiting counts toward the caller's optional-count timeout. Cancellation releases the
    // permit and kills children, so decoration cannot build an unbounded work backlog.
    let _slot = NUMSTAT_SLOTS
        .acquire()
        .await
        .expect("numstat slots stay open");
    let mut tracked = Vec::new();
    for row in rows.iter().filter(|row| row.status != "??") {
        tracked.push(row.path.as_str());
        if let Some(source) = row.source.as_deref() {
            tracked.push(source);
        }
    }
    let mut complete = true;
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
            Err(_) => match head_exists(root).await {
                Ok(false) => match run_paths(
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
                {
                    Ok(s) => (s, false),
                    Err(_) => {
                        complete = false;
                        (String::new(), false)
                    }
                },
                _ => {
                    complete = false;
                    (String::new(), true)
                }
            },
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
        } else {
            complete = false;
        }
    }

    Counts {
        by_path: counts,
        complete,
    }
}

async fn head_exists(root: &Path) -> std::io::Result<bool> {
    let out = inspection_command(root)
        .args(["rev-parse", "--verify", "-q", "HEAD"])
        .env("GIT_OPTIONAL_LOCKS", "0")
        .output()
        .await?;
    match out.status.code() {
        Some(0) => Ok(true),
        Some(1) => Ok(false),
        _ => Err(std::io::Error::other(
            String::from_utf8_lossy(&out.stderr).trim().to_string(),
        )),
    }
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

/// `git` in the repository, with its own environment kept out of the way. A pager here would wait
/// for a terminal nobody has, and a locale would translate the words this parses.
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
        // The counts occupy the first two tab-separated fields. Keep the rest intact because
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
mod tests;
