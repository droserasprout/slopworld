//! Directory listings and Git-ignore classification for the Files tree and project picker.
use super::super::{ApiResult, Mgr, err};
use crate::api::protobuf::Proto;
use crate::api::protobuf::path_text;
use crate::api::types::BrowseReq;
use crate::shared::wire;
use axum::{
    extract::{Query, State},
    http::StatusCode,
};
use std::time::Duration;

pub(crate) const BROWSE_LIMIT: usize = 500;
const GITIGNORE_TIMEOUT: Duration = Duration::from_secs(3);

pub(crate) fn browse_limit(requested: Option<usize>) -> usize {
    requested.unwrap_or(BROWSE_LIMIT).clamp(1, BROWSE_LIMIT)
}

#[derive(Default)]
pub(crate) struct Listing {
    pub(crate) dirs: Vec<String>,
    pub(crate) empty_dirs: Vec<String>,
    pub(crate) gitignored_dirs: Vec<String>,
    pub(crate) files: Vec<String>,
    pub(crate) gitignored_files: Vec<String>,
    pub(crate) truncated: bool,
}

impl Listing {
    fn len(&self) -> usize {
        self.dirs.len() + self.files.len()
    }

    fn append_batch(&mut self, mut batch: Self, limit: usize) {
        let remaining = limit.saturating_sub(self.len());
        // Only an additional visible entry proves truncation, even across batches.
        self.truncated = batch.len() > remaining;
        batch.dirs.truncate(remaining);
        batch.files.truncate(remaining - batch.dirs.len());
        self.empty_dirs.extend(
            batch
                .empty_dirs
                .into_iter()
                .filter(|name| batch.dirs.contains(name)),
        );
        self.gitignored_dirs.extend(
            batch
                .gitignored_dirs
                .into_iter()
                .filter(|name| batch.dirs.contains(name)),
        );
        self.gitignored_files.extend(
            batch
                .gitignored_files
                .into_iter()
                .filter(|name| batch.files.contains(name)),
        );
        self.dirs.extend(batch.dirs);
        self.files.extend(batch.files);
    }
}

/// Scan bounded batches so ignored entries never consume the visible result limit.
pub(crate) async fn list_dir(
    base: &std::path::Path,
    want_files: bool,
    hidden: bool,
    limit: usize,
    filter: &str,
    hide_ignored: bool,
) -> std::io::Result<Listing> {
    let mut out = Listing::default();
    let mut rd = tokio::fs::read_dir(base).await?;
    loop {
        let batch_limit = if hide_ignored {
            BROWSE_LIMIT
        } else {
            limit.saturating_add(1)
        };
        let mut batch = read_batch(&mut rd, want_files, hidden, batch_limit, filter).await?;
        let exhausted = batch.len() < batch_limit;
        filter_gitignored(base, &mut batch, hide_ignored).await;
        out.append_batch(batch, limit);
        if exhausted || out.truncated {
            break;
        }
    }
    out.dirs.sort();
    out.empty_dirs.sort();
    out.files.sort();
    out.gitignored_dirs.sort();
    out.gitignored_files.sort();
    Ok(out)
}

async fn read_batch(
    rd: &mut tokio::fs::ReadDir,
    want_files: bool,
    hidden: bool,
    limit: usize,
    filter: &str,
) -> std::io::Result<Listing> {
    let mut out = Listing::default();
    while out.len() < limit {
        let Some(e) = rd.next_entry().await? else {
            break;
        };
        // Wire names are used for mutations, so replacement characters must never
        // turn an unsupported filename into the identity of a different entry.
        let name = e.file_name().into_string().map_err(|_name| std::io::Error::new(
            std::io::ErrorKind::InvalidData,
            "This directory contains a filename that is not valid UTF-8. Rename it using a terminal.",
        ))?;
        if (!hidden && name.starts_with('.')) || !name.contains(filter) {
            continue;
        }
        let t = e.file_type().await?;
        let is_dir = if t.is_symlink() {
            match tokio::fs::metadata(e.path()).await {
                Ok(m) => m.is_dir(),
                Err(_) => continue,
            }
        } else {
            t.is_dir()
        };
        if is_dir {
            if want_files && matches!(directory_is_empty(&e.path(), hidden).await, Ok(true)) {
                out.empty_dirs.push(name.clone());
            }
            out.dirs.push(name);
        } else if want_files {
            out.files.push(name);
        }
    }
    Ok(out)
}

/// Check whether a directory contains entries that the Files tree can show.
/// The caller treats failed checks as unknown and preserves the expansion arrow.
/// Thus, permission errors do not make a directory appear empty.
async fn directory_is_empty(path: &std::path::Path, hidden: bool) -> std::io::Result<bool> {
    let mut rd = tokio::fs::read_dir(path).await?;
    while let Some(e) = rd.next_entry().await? {
        let name = e.file_name().to_string_lossy().into_owned();
        if !hidden && name.starts_with('.') {
            continue;
        }

        let t = e.file_type().await?;
        if t.is_symlink() && tokio::fs::metadata(e.path()).await.is_err() {
            continue;
        }
        return Ok(false);
    }
    Ok(true)
}

/// Classify entries with `git check-ignore` in the listed directory.
/// Skip classification without an error if Git is unavailable or the path is outside a repository.
/// When `hide` is false, keep ignored entries so the Files tab can show them with a different color.
pub(crate) async fn filter_gitignored(base: &std::path::Path, listing: &mut Listing, hide: bool) {
    if listing.dirs.is_empty() && listing.files.is_empty() {
        return;
    }

    let mut cmd = crate::git::inspection_command(base);
    cmd.current_dir(base)
        .args(["check-ignore", "--stdin", "-z"])
        .env("GIT_OPTIONAL_LOCKS", "0")
        .env("GIT_PAGER", "cat")
        .env("GIT_TERMINAL_PROMPT", "0")
        .env("LC_ALL", "C")
        .stdout(std::process::Stdio::piped());

    let mut input = Vec::new();
    for name in listing.dirs.iter().chain(listing.files.iter()) {
        input.extend_from_slice(name.as_bytes());
        input.push(0);
    }

    let Ok(output) = crate::process::run_bounded_with_stdin(
        &mut cmd,
        &input,
        GITIGNORE_TIMEOUT,
        crate::process::CaptureLimits {
            // `check-ignore -z` echoes a subset of the NUL-delimited names it receives.
            // Retaining at most the complete request is enough for a valid response while the
            // helper continues draining an unexpectedly verbose Git process.
            stdout: input.len(),
            stderr: 16 * 1024,
        },
    )
    .await
    else {
        return;
    };

    // check-ignore exits 1 when no path matched. Any other failure, including output overflow,
    // means the metadata is unavailable: applying a partial classification could hide entries.
    if !(output.status.success() || output.status.code() == Some(1))
        || output.stdout_truncated
        || output.stderr_truncated
    {
        return;
    }
    if !output.stdout.is_empty() && output.stdout.last() != Some(&0) {
        return;
    }

    let mut ignored = std::collections::HashSet::new();
    let known = listing
        .dirs
        .iter()
        .chain(listing.files.iter())
        .cloned()
        .collect::<std::collections::HashSet<_>>();
    let body = output.stdout.strip_suffix(&[0]).unwrap_or(&output.stdout);
    for chunk in body.split(|&b| b == 0) {
        if chunk.is_empty() {
            return;
        }
        let Ok(s) = std::str::from_utf8(chunk) else {
            return;
        };
        if !known.contains(s) {
            return;
        }
        ignored.insert(s.to_owned());
    }

    listing.gitignored_dirs = listing
        .dirs
        .iter()
        .filter(|n| ignored.contains(*n))
        .cloned()
        .collect();
    listing.gitignored_files = listing
        .files
        .iter()
        .filter(|n| ignored.contains(*n))
        .cloned()
        .collect();

    if hide {
        listing.dirs.retain(|n| !ignored.contains(n));
        listing.empty_dirs.retain(|n| !ignored.contains(n));
        listing.files.retain(|n| !ignored.contains(n));
    }
}

/// Supply directory listings for project selection and the Files tree without direct host filesystem access from the mod.
/// Always include directories. Include files only when requested.
pub(crate) async fn browse(
    State(_m): State<Mgr>,
    Query(q): Query<BrowseReq>,
) -> ApiResult<wire::BrowseResult> {
    let _perf = crate::perf::timer("http-browse");
    let base = if q.path.is_empty() {
        dirs::home_dir().unwrap_or_else(|| "/".into())
    } else {
        std::path::PathBuf::from(crate::config::expand(&q.path))
    };

    let limit = browse_limit(q.limit);
    let out = list_dir(&base, q.files, q.hidden, limit, &q.filter, q.gitignore)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;

    crate::perf::count(
        "http-browse-rows",
        (out.dirs.len() + out.files.len()) as u64,
    );

    Ok(Proto(wire::BrowseResult {
        path: path_text(&base)?,
        parent: base.parent().map(path_text).transpose()?,
        dirs: out.dirs,
        empty_dirs: out.empty_dirs,
        gitignored_dirs: out.gitignored_dirs,
        files: out.files,
        gitignored_files: out.gitignored_files,
        truncated: out.truncated,
    }))
}

#[cfg(test)]
#[path = "browse_tests.rs"]
mod tests;
