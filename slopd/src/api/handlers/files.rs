//! Files, preview, search, and Git HTTP boundaries.
use crate::api::protobuf::{domain, reply, Proto};
use crate::shared::wire;

use anyhow::{bail, Context, Result};
use axum::extract::{Query, State};
use axum::http::StatusCode;

use serde_json::{json, Value};
use std::{
    path::{Path, PathBuf},
    time::Duration,
};
use tokio::io::{AsyncBufRead, AsyncReadExt};
use tokio::process::Command;

use super::super::types::*;
use super::{err, ApiResult, Mgr};

/// Limit preview response size to bound memory use in the daemon and game.
/// Generated documents can exceed the size needed for a UI preview.
pub(crate) const READ_LIMIT: u64 = 512 * 1024;

/// Limit image preview size to bound response memory use while permitting screenshots.
pub(crate) const IMAGE_LIMIT: u64 = 8 * 1024 * 1024;

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
        if !hidden && name.starts_with('.') {
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

    let output = match crate::process::run_bounded_with_stdin(
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
    {
        Ok(output) => output,
        Err(_) => return,
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
    let mut out = list_dir(&base, q.files, q.hidden, limit)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;

    filter_gitignored(&base, &mut out, q.gitignore).await;
    crate::perf::count(
        "http-browse-rows",
        (out.dirs.len() + out.files.len()) as u64,
    );

    reply(json!({
        "path": base,
        "parent": base.parent(),
        "dirs": out.dirs,
        "empty_dirs": out.empty_dirs,
        "gitignored_dirs": out.gitignored_dirs,
        "files": out.files,
        "gitignored_files": out.gitignored_files,
        "truncated": out.truncated,
    }))
}

#[derive(Clone, Copy)]
struct ReaderStamp {
    device: u64,
    inode: u64,
    length: u64,
    mtime: i64,
    mtime_nsec: i64,
    ctime: i64,
    ctime_nsec: i64,
}

impl ReaderStamp {
    fn from_metadata(metadata: &std::fs::Metadata) -> Self {
        use std::os::unix::fs::MetadataExt;
        Self {
            device: metadata.dev(),
            inode: metadata.ino(),
            length: metadata.len(),
            mtime: metadata.mtime(),
            mtime_nsec: metadata.mtime_nsec(),
            ctime: metadata.ctime(),
            ctime_nsec: metadata.ctime_nsec(),
        }
    }

    fn encode(self) -> String {
        format!(
            "{}:{}:{}:{}:{}:{}:{}",
            self.device,
            self.inode,
            self.length,
            self.mtime,
            self.mtime_nsec,
            self.ctime,
            self.ctime_nsec
        )
    }
}

// Do not infer file deletion from filtered or truncated directory listings.
// Close a reader only when its path is missing or is not a file.
// Retry after permission and I/O errors.
async fn reader_stat(path: &Path) -> std::io::Result<wire::FileStatResult> {
    match tokio::fs::metadata(path).await {
        Ok(metadata) => {
            let is_file = metadata.is_file();
            // Include inode and change time to catch atomic saves and same-size rewrites.
            let stamp = if is_file {
                ReaderStamp::from_metadata(&metadata).encode()
            } else {
                String::new()
            };
            Ok(wire::FileStatResult { is_file, stamp })
        }
        Err(error)
            if matches!(
                error.kind(),
                std::io::ErrorKind::NotFound | std::io::ErrorKind::NotADirectory
            ) =>
        {
            Ok(wire::FileStatResult::default())
        }
        Err(error) => Err(error),
    }
}

pub(crate) async fn file_stat(
    State(_m): State<Mgr>,
    Query(q): Query<ReadReq>,
) -> ApiResult<wire::FileStatResult> {
    let path = &q.path;
    if path.trim().is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "A path is required."));
    }
    let path = std::path::PathBuf::from(crate::config::expand(path));
    let stat = reader_stat(&path)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(crate::api::protobuf::Proto(stat))
}

pub(crate) async fn read_file(
    State(_m): State<Mgr>,
    Query(q): Query<ReadReq>,
) -> ApiResult<wire::TextResult> {
    let path = q.path.trim();
    if path.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "A path is required."));
    }

    let path = std::path::PathBuf::from(crate::config::expand(path));
    let text = read_preview(&path)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    reply(json!({
        "path": path,
        "text": text,
        "bytes": text.len(),
    }))
}

pub(crate) const HIGHLIGHT_LIMIT: usize = READ_LIMIT as usize * 4;
pub(crate) const HIGHLIGHT_TIMEOUT: Duration = Duration::from_secs(3);

/// Own the temporary input even when the request future is cancelled during a write or process run.
struct HighlightTempFile(PathBuf);

impl Drop for HighlightTempFile {
    fn drop(&mut self) {
        drop(std::fs::remove_file(&self.0));
    }
}

pub(crate) async fn highlight(
    State(m): State<Mgr>,
    Proto(q): Proto<wire::HighlightReq>,
) -> ApiResult<wire::TextResult> {
    let q: HighlightReq = domain(q)?;
    if q.text.len() as u64 > READ_LIMIT {
        return Err(err(
            StatusCode::BAD_REQUEST,
            "The code is larger than the preview limit.",
        ));
    }
    let command = q.command.unwrap_or(m.config().await.commands.highlighter);
    let command = super::highlighting::themed_command(&command, &q.engine, &q.theme)
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let text = highlight_text(&command, &q.language, &q.text)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "text": text, "bytes": text.len() }))
}

async fn highlight_text(command: &str, language: &str, text: &str) -> anyhow::Result<String> {
    if command.trim().is_empty() {
        bail!("The syntax highlighter is disabled.");
    }

    let dir = std::path::PathBuf::from(crate::paths::temp_dir("highlight"));
    tokio::fs::create_dir_all(&dir).await?;
    let extension: String = language
        .trim()
        .trim_start_matches('.')
        .chars()
        .take_while(|c| c.is_ascii_alphanumeric() || matches!(c, '+' | '-' | '_'))
        .take(24)
        .collect();
    let extension = if extension.is_empty() {
        "txt"
    } else {
        &extension
    };
    let path = dir.join(format!("{}.{}", uuid::Uuid::new_v4(), extension));
    let cleanup = HighlightTempFile(path);
    tokio::fs::write(&cleanup.0, text).await?;

    let result = run_highlighter(command, &cleanup.0).await;
    drop(cleanup);
    result
}

pub(crate) fn highlighter_argv(
    command: &str,
    path: &std::path::Path,
) -> anyhow::Result<Vec<String>> {
    let mut argv = crate::sandbox::shell_split(command);
    if argv.is_empty() {
        bail!("The syntax highlighter is disabled.");
    }
    let path = path
        .to_str()
        .ok_or_else(|| anyhow::anyhow!("The temporary file path is not valid UTF-8."))?;
    let mut replaced = false;
    for arg in &mut argv {
        if arg.contains("%s") {
            *arg = arg.replace("%s", path);
            replaced = true;
        }
    }
    if !replaced {
        argv.push(path.to_string());
    }
    Ok(argv)
}

async fn run_highlighter(command: &str, path: &std::path::Path) -> anyhow::Result<String> {
    let argv = highlighter_argv(command, path)?;
    run_highlighter_argv(&argv).await
}

pub(super) async fn run_highlighter_argv(argv: &[String]) -> anyhow::Result<String> {
    let Some((program, args)) = argv.split_first() else {
        bail!("The syntax highlighter command is empty.");
    };
    let mut process = Command::new(program);
    process.args(args);
    let output = crate::process::run_bounded(
        &mut process,
        HIGHLIGHT_TIMEOUT,
        crate::process::CaptureLimits {
            stdout: HIGHLIGHT_LIMIT,
            stderr: 16 * 1024,
        },
    )
    .await
    .map_err(|error| {
        if error.downcast_ref::<crate::process::TimedOut>().is_some() {
            anyhow::anyhow!("The syntax highlighter timed out.")
        } else {
            error.context("The daemon could not start the syntax highlighter.")
        }
    })?;
    if output.stdout_truncated {
        bail!("The syntax highlighter output exceeds the preview limit.");
    }
    if !output.status.success() {
        bail!(
            "The syntax highlighter failed: {}",
            String::from_utf8_lossy(&output.stderr).trim()
        );
    }
    String::from_utf8(output.stdout).context("The syntax highlighter output is not valid UTF-8.")
}

pub(crate) async fn read_image(
    State(_m): State<Mgr>,
    Query(q): Query<ReadReq>,
) -> ApiResult<wire::ImageResult> {
    let path = q.path.trim();
    if path.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "A path is required."));
    }

    let path = std::path::PathBuf::from(crate::config::expand(path));
    let bytes = read_image_bytes(&path)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    reply(json!({
        "path": path,
        "data": bytes,
        "bytes": bytes.len(),
    }))
}

async fn read_file_bounded(path: &Path, limit: u64, label: &str) -> Result<Vec<u8>> {
    let metadata = tokio::fs::metadata(path).await?;
    if !metadata.is_file() {
        bail!("The path does not identify a file.");
    }
    let (unit, unit_size) = if label == "image" {
        ("MiB", 1024_u64 * 1024)
    } else {
        ("KiB", 1024_u64)
    };
    if metadata.len() > limit {
        bail!(
            "The {label} is larger than the preview limit of {} {}.",
            limit / unit_size,
            unit
        );
    }

    let file = tokio::fs::File::open(path).await?;
    let mut bytes = Vec::with_capacity(metadata.len().min(limit) as usize);
    file.take(limit.saturating_add(1))
        .read_to_end(&mut bytes)
        .await?;
    if bytes.len() as u64 > limit {
        bail!(
            "The {label} exceeded the preview limit of {} {} during the read.",
            limit / unit_size,
            unit
        );
    }
    Ok(bytes)
}

pub(crate) async fn read_image_bytes(path: &Path) -> Result<Vec<u8>> {
    read_file_bounded(path, IMAGE_LIMIT, "image").await
}

pub(crate) async fn read_preview(path: &Path) -> Result<String> {
    let bytes = read_file_bounded(path, READ_LIMIT, "file").await?;
    String::from_utf8(bytes).context("The file is not valid UTF-8.")
}

pub(crate) fn file_path(path: &str) -> anyhow::Result<std::path::PathBuf> {
    let path = crate::config::expand(path);
    if path.trim().is_empty() {
        bail!("A path is required.");
    }

    let path = std::path::PathBuf::from(path);
    if !path.is_absolute() {
        bail!("Use an absolute path.");
    }
    use std::os::unix::ffi::OsStrExt;
    if path
        .as_os_str()
        .as_bytes()
        .split(|byte| *byte == b'/')
        .any(|part| part == b"." || part == b"..")
    {
        bail!("Use a path without . or .. components.");
    }
    Ok(path)
}

pub(crate) fn entry_name(name: &str) -> anyhow::Result<&str> {
    let name = name.trim();
    if name.is_empty() || name == "." || name == ".." {
        bail!("Enter a name other than . or ..");
    }
    if name.contains('/') || name.contains('\\') || name.contains('\0') {
        bail!("Enter one file or folder name without path separators or null characters.");
    }
    Ok(name)
}

pub(crate) async fn create_file(Proto(q): Proto<wire::FileReq>) -> ApiResult<wire::Ack> {
    let q: FileReq = domain(q)?;
    let parent = file_path(&q.path).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let name = entry_name(&q.name).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let meta = tokio::fs::metadata(&parent)
        .await
        .with_context(|| {
            format!(
                "The daemon could not read the parent directory {}.",
                parent.display()
            )
        })
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    if !meta.is_dir() {
        return Err(err(
            StatusCode::BAD_REQUEST,
            format!("The parent path is not a directory: {}.", parent.display()),
        ));
    }

    let target = parent.join(name);
    if tokio::fs::symlink_metadata(&target).await.is_ok() {
        return Err(err(
            StatusCode::BAD_REQUEST,
            format!("A path already exists: {}.", target.display()),
        ));
    }

    let result = if q.kind.trim() == "file" {
        tokio::fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&target)
            .await
            .map(|_| ())
            .with_context(|| format!("The daemon could not create {}.", target.display()))
    } else if q.kind.trim() == "folder" {
        tokio::fs::create_dir(&target)
            .await
            .with_context(|| format!("The daemon could not create {}.", target.display()))
    } else {
        return Err(err(StatusCode::BAD_REQUEST, "Set kind to file or folder."));
    };
    result.map_err(|e| err(StatusCode::BAD_REQUEST, e))?;

    reply(json!({ "ok": true }))
}

pub(crate) async fn rename_file(Proto(q): Proto<wire::FileReq>) -> ApiResult<wire::Ack> {
    let q: FileReq = domain(q)?;
    let source = file_path(&q.path).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let name = entry_name(&q.name).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let parent = source
        .parent()
        .filter(|p| !p.as_os_str().is_empty())
        .ok_or_else(|| err(StatusCode::BAD_REQUEST, "You cannot rename this path."))?;
    tokio::fs::symlink_metadata(&source)
        .await
        .with_context(|| format!("The daemon could not read {}.", source.display()))
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;

    let target = parent.join(name);
    if tokio::fs::symlink_metadata(&target).await.is_ok() {
        return Err(err(
            StatusCode::BAD_REQUEST,
            format!("A path already exists: {}.", target.display()),
        ));
    }
    // The earlier existence check is only for a friendly message. The kernel
    // must enforce no replacement at commit time because another writer can race it.
    nix::fcntl::renameat2(
        None,
        &source,
        None,
        &target,
        nix::fcntl::RenameFlags::RENAME_NOREPLACE,
    )
    .with_context(|| format!("The daemon could not rename {}.", source.display()))
    .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "ok": true }))
}

pub(crate) async fn remove_file(Proto(q): Proto<wire::FileReq>) -> ApiResult<wire::Ack> {
    let q: FileReq = domain(q)?;
    let path = file_path(&q.path).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let meta = tokio::fs::symlink_metadata(&path)
        .await
        .with_context(|| format!("The daemon could not read {}.", path.display()))
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    if path.parent().is_none() {
        return Err(err(StatusCode::BAD_REQUEST, "You cannot remove this path."));
    }

    if meta.is_dir() {
        tokio::fs::remove_dir_all(&path)
            .await
            .with_context(|| format!("The daemon could not remove {}.", path.display()))
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    } else {
        tokio::fs::remove_file(&path)
            .await
            .with_context(|| format!("The daemon could not remove {}.", path.display()))
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    }
    reply(json!({ "ok": true }))
}

pub(crate) const SEARCH_LIMIT: usize = 200;
/// Bound memory used for one ripgrep JSON event, including escaped match text.
pub(crate) const SEARCH_RECORD_LIMIT: usize = 64 * 1024;
// `rg --max-columns` limits text output, but JSON matches contain complete lines.
// Generated or minified files can produce sidebar rows with tens of thousands of characters.
// Limit preview length. Center the preview near the first match when space permits.
pub(crate) const SEARCH_TEXT_LIMIT: usize = 1_000;

pub(crate) fn search_preview(text: &str, column: usize) -> String {
    let text = text.trim_end_matches(['\r', '\n']);
    if text.len() <= SEARCH_TEXT_LIMIT {
        return text.to_string();
    }

    // `column` is a byte offset from ripgrep. Do not slice through a UTF-8 codepoint even when
    // a match began at a non-ASCII character.
    let boundary = |at: usize| {
        let mut at = at.min(text.len());
        while at > 0 && !text.is_char_boundary(at) {
            at -= 1;
        }
        at
    };
    let match_at = boundary(column.saturating_sub(1));
    let begin = boundary(match_at.saturating_sub(SEARCH_TEXT_LIMIT / 2));
    let end = boundary((begin + SEARCH_TEXT_LIMIT).min(text.len()));
    let mut out = String::with_capacity(end - begin + 6);
    if begin > 0 {
        out.push('…');
    }
    if let Some(preview) = text.get(begin..end) {
        out.push_str(preview);
    }
    if end < text.len() {
        out.push('…');
    }
    out
}

pub(crate) fn build_rg_command(path: &std::path::Path, q: &SearchReq) -> tokio::process::Command {
    let mut cmd = tokio::process::Command::new("rg");
    cmd.current_dir(path)
        .arg("--json")
        .arg("--line-number")
        .arg("--color=never")
        .arg("--max-columns=1000")
        .arg("--max-columns-preview")
        .arg("--no-messages");
    if !q.regex {
        cmd.arg("--fixed-strings");
    }
    if q.case {
        cmd.arg("--case-sensitive");
    } else {
        cmd.arg("--ignore-case");
    }
    if q.word {
        cmd.arg("--word-regexp");
    }
    if !q.gitignore {
        cmd.arg("--no-ignore");
    }
    if q.hidden {
        cmd.arg("--hidden");
    }
    cmd.arg("--glob=!.git/**")
        .arg("--")
        .arg(&q.q)
        .arg(".")
        .stdout(std::process::Stdio::piped())
        .stderr(std::process::Stdio::null())
        .kill_on_drop(true);
    cmd
}

enum SearchRecord {
    Line(Vec<u8>),
    TooLarge,
}

/// Read one newline-delimited record without retaining more than `limit` bytes.
/// An oversized record is left unread so the caller can stop its child immediately.
async fn read_search_record<R: AsyncBufRead + Unpin>(
    reader: &mut R,
    limit: usize,
) -> std::io::Result<Option<SearchRecord>> {
    use tokio::io::AsyncBufReadExt;

    let mut record = Vec::with_capacity(limit.min(4096));
    loop {
        let available = reader.fill_buf().await?;
        if available.is_empty() {
            return Ok(if record.is_empty() {
                None
            } else {
                Some(SearchRecord::Line(record))
            });
        }

        let newline = available.iter().position(|byte| *byte == b'\n');
        let count = newline.map_or(available.len(), |index| index + 1);
        if record.len().saturating_add(count) > limit {
            return Ok(Some(SearchRecord::TooLarge));
        }
        let Some(chunk) = available.get(..count) else {
            return Err(std::io::Error::new(
                std::io::ErrorKind::InvalidData,
                "ripgrep output length exceeded the available bytes",
            ));
        };
        record.extend_from_slice(chunk);
        reader.consume(count);
        if newline.is_some() {
            return Ok(Some(SearchRecord::Line(record)));
        }
    }
}

async fn stop_search_child(child: &mut tokio::process::Child) {
    drop(child.kill().await);
    drop(child.wait().await);
}

fn search_match(record: &Value) -> Value {
    let data = record.get("data");
    let path = data
        .and_then(|data| data.get("path"))
        .and_then(|path| path.get("text"))
        .and_then(Value::as_str)
        .unwrap_or("")
        .trim_start_matches("./");
    let text = data
        .and_then(|data| data.get("lines"))
        .and_then(|lines| lines.get("text"))
        .and_then(Value::as_str)
        .unwrap_or("");
    let column = data
        .and_then(|data| data.get("submatches"))
        .and_then(Value::as_array)
        .and_then(|submatches| submatches.first())
        .and_then(|submatch| submatch.get("start"))
        .and_then(Value::as_u64)
        .and_then(|column| usize::try_from(column).ok())
        .unwrap_or(0)
        .saturating_add(1);
    json!({
        "path": path,
        "line": data.and_then(|data| data.get("line_number")).and_then(Value::as_u64).unwrap_or(0),
        "column": column,
        "text": search_preview(text, column),
    })
}

/// Search one project without sending patterns or paths through a shell.
/// `rg --json` distinguishes filenames from matching text.
/// Bound each record while reading and stop at the UI or record limit.
/// This bounds the memory needed for repository search results.
pub(crate) async fn search(
    State(_m): State<Mgr>,
    Query(q): Query<SearchReq>,
) -> ApiResult<wire::SearchResult> {
    let _perf = crate::perf::timer("http-search");
    use tokio::io::BufReader;

    if q.path.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "A path is required."));
    }
    if q.q.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "A search query is required."));
    }

    let dir = std::path::PathBuf::from(crate::config::expand(&q.path));
    let limit = q.limit.unwrap_or(SEARCH_LIMIT).clamp(1, SEARCH_LIMIT);

    let mut child = build_rg_command(&dir, &q).spawn().map_err(|e| {
        err(
            StatusCode::BAD_REQUEST,
            format!("The daemon could not start ripgrep: {e}"),
        )
    })?;
    let Some(stdout) = child.stdout.take() else {
        stop_search_child(&mut child).await;
        return Err(err(
            StatusCode::INTERNAL_SERVER_ERROR,
            "The daemon could not read ripgrep output.",
        ));
    };
    let mut reader = BufReader::new(stdout);
    let mut matches = Vec::new();
    let mut truncated = false;

    loop {
        let record = match read_search_record(&mut reader, SEARCH_RECORD_LIMIT).await {
            Ok(record) => record,
            Err(error) => {
                stop_search_child(&mut child).await;
                return Err(err(StatusCode::BAD_REQUEST, error));
            }
        };
        let Some(record) = record else {
            break;
        };
        let record = match record {
            SearchRecord::Line(record) => record,
            SearchRecord::TooLarge => {
                // Return the useful prefix and report that the unprocessed search is incomplete.
                truncated = true;
                stop_search_child(&mut child).await;
                break;
            }
        };
        let Ok(v) = serde_json::from_slice::<Value>(&record) else {
            continue;
        };
        if v.get("type").and_then(Value::as_str) != Some("match") {
            continue;
        }
        if matches.len() >= limit {
            truncated = true;
            stop_search_child(&mut child).await;
            break;
        }

        matches.push(search_match(&v));
    }

    if !truncated {
        let status = child
            .wait()
            .await
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
        // ripgrep uses 1 for a clean search with no matches.
        if !status.success() && status.code() != Some(1) {
            return Err(err(
                StatusCode::BAD_REQUEST,
                format!("Ripgrep exited with status {status}."),
            ));
        }
    }

    crate::perf::count("http-search-matches", matches.len() as u64);

    reply(json!({
        "path": dir,
        "matches": matches,
        "truncated": truncated,
    }))
}

/// Return a project's changed files for the Git view.
/// The game cannot run `git` inside agent mount namespaces.
/// Paths outside repositories return 200 with `repo: false`.
pub(crate) async fn git_status(
    State(_m): State<Mgr>,
    Query(q): Query<GitReq>,
) -> ApiResult<wire::GitResult> {
    let _perf = crate::perf::timer("http-git");
    if q.path.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "A path is required."));
    }
    let dir = std::path::PathBuf::from(crate::config::expand(&q.path));

    let out = crate::git::status_with_counts(&dir, q.counts.unwrap_or(true))
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;

    let Some(st) = out else {
        return reply(json!({ "repo": false, "path": dir }));
    };
    crate::perf::count("http-git-rows", st.changes.len() as u64);

    reply(json!({
        "repo": true,
        "root": st.root,
        "branch": st.branch,
        "changed": st.changed,
        "added": st.added,
        "deleted": st.deleted,
        "truncated": st.truncated,
        "counts_complete": st.counts_complete,
        "files": st.changes.iter().map(|c| json!({
            "path": c.path,
            "status": c.status,
            "added": c.added,
            "deleted": c.deleted,
        })).collect::<Vec<_>>(),
    }))
}

#[cfg(test)]
#[path = "files_tests.rs"]
mod mutation_tests;

#[cfg(test)]
#[path = "files_reader_tests.rs"]
mod reader_tests;
