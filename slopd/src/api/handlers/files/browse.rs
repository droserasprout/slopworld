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

pub(crate) struct Listing {
    pub(crate) dirs: Vec<String>,
    pub(crate) empty_dirs: Vec<String>,
    pub(crate) gitignored_dirs: Vec<String>,
    pub(crate) files: Vec<String>,
    pub(crate) gitignored_files: Vec<String>,
    pub(crate) truncated: bool,
}

pub(crate) async fn list_dir(
    base: &std::path::Path,
    want_files: bool,
    hidden: bool,
    limit: usize,
    filter: &str,
) -> std::io::Result<Listing> {
    let mut out = Listing {
        dirs: Vec::new(),
        empty_dirs: Vec::new(),
        gitignored_dirs: Vec::new(),
        files: Vec::new(),
        gitignored_files: Vec::new(),
        truncated: false,
    };

    let mut rd = tokio::fs::read_dir(base).await?;
    while let Some(e) = rd.next_entry().await? {
        let name = e.file_name().to_string_lossy().into_owned();
        if (!hidden && name.starts_with('.')) || !name.contains(filter) {
            continue;
        }

        let t = e.file_type().await?;
        // `DirEntry::file_type` identifies symbolic links without following them.
        // Read target metadata to classify linked directories correctly.
        // Exclude a link if its target metadata is unavailable.
        let is_dir = if t.is_symlink() {
            match tokio::fs::metadata(e.path()).await {
                Ok(m) => m.is_dir(),
                Err(_) => continue,
            }
        } else {
            t.is_dir()
        };

        if !is_dir && !want_files {
            // Exclude unrequested files from the count.
            // A directory with three subdirectories and ten thousand files then returns three rows without truncation.
            continue;
        }

        let count = out.dirs.len() + out.files.len();
        if count >= limit {
            // This additional matching entry confirms truncation.
            // Reaching the limit alone does not prove that more entries exist.
            out.truncated = true;
            break;
        }

        if is_dir {
            // The Files tree must distinguish unopened directories from empty directories.
            // Check only when the request includes files. The project directory selector does not need this metadata.
            if want_files && matches!(directory_is_empty(&e.path(), hidden).await, Ok(true)) {
                out.empty_dirs.push(name.clone());
            }
            out.dirs.push(name);
        } else {
            out.files.push(name);
        }
    }

    out.dirs.sort();
    out.empty_dirs.sort();
    out.files.sort();
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
    let mut out = list_dir(&base, q.files, q.hidden, limit, &q.filter)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;

    filter_gitignored(&base, &mut out, q.gitignore).await;
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
