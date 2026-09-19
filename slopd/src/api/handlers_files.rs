//! Files, preview, search, and Git HTTP boundaries.
use crate::api::protobuf::{domain, reply, Proto};
use crate::shared::wire;

use anyhow::{bail, Context, Result};
use axum::extract::{Query, State};
use axum::http::StatusCode;

use serde_json::{json, Value};
use std::{path::Path, time::Duration};
use tokio::io::AsyncReadExt;
use tokio::process::Command;

use super::super::types::*;
use super::{err, ApiResult, Mgr};

/// A preview is a UI document, not a file transfer. Keep the response bounded so a generated
/// README cannot turn one click into an unbounded JSON allocation in the daemon or the game.
pub(crate) const READ_LIMIT: u64 = 512 * 1024;

/// Images are a bounded preview transfer, not a general file download. This leaves room for
/// screenshots while keeping one Markdown page from allocating an unbounded JSON response.
pub(crate) const IMAGE_LIMIT: u64 = 8 * 1024 * 1024;

/// Enough to fill a column several screens deep, and short of the answer to
/// `read_dir` on `.git` or `node_modules` being a reply nobody reads.
pub(crate) const BROWSE_LIMIT: usize = 500;
const GITIGNORE_TIMEOUT: Duration = Duration::from_secs(3);

pub(crate) fn browse_limit(requested: Option<usize>) -> usize {
    requested.unwrap_or(BROWSE_LIMIT).clamp(1, BROWSE_LIMIT)
}

/// What one directory holds, as far as this endpoint is concerned. Split out from the
/// handler so the rules below can be tested against a real directory without standing a
/// manager and a router up around them.
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
        // `DirEntry::file_type` is an lstat: a symlink is a symlink and never the thing it
        // points at, so a symlinked directory would come out neither a dir nor a file and
        // read in the tree as one that had gone. One stat per link, and a broken one falls
        // out of both lists rather than being guessed at.
        let is_dir = if t.is_symlink() {
            match tokio::fs::metadata(e.path()).await {
                Ok(m) => m.is_dir(),
                Err(_) => continue,
            }
        } else {
            t.is_dir()
        };

        if !is_dir && !want_files {
            // Not asked for, so not counted either: a directory holding three folders and
            // ten thousand files is three rows, not a truncated answer.
            continue;
        }

        let count = out.dirs.len() + out.files.len();
        if count >= limit {
            // This qualifying entry is the evidence that the answer really was cut short.
            // Merely reaching the limit is not: a directory may contain exactly that many.
            out.truncated = true;
            break;
        }

        if is_dir {
            // The Files tree needs to distinguish a directory that has not been opened yet
            // from one that is genuinely empty. Probe only when files are requested: the
            // project-dir picker asks for directories alone and does not need this metadata.
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

/// Whether a directory has any child the Files tree would show. A failed child stat is left as
/// an unknown result by the caller, so a permission problem keeps the disclosure arrow rather
/// than falsely claiming that the directory is empty.
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

/// Classify entries that `.gitignore` rules cover. Runs `git check-ignore` in the listed
/// directory; silently skips classification when git is absent or the path is outside a repo.
/// When `hide` is false, the classified entries stay in the listing so the Files tab can tint
/// them while showing them.
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

/// So the dialog can pick a project dir, and the files view can draw a tree, without
/// the mod touching the host filesystem itself. `dirs` is what it always was; `files`
/// is asked for.
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

// Reader lifetime must not infer deletion from filtered or truncated directory listings.
// Only a definitive missing/non-file result closes a reader; permission and I/O errors retry.
async fn reader_is_file(path: &Path) -> std::io::Result<bool> {
    match tokio::fs::metadata(path).await {
        Ok(metadata) => Ok(metadata.is_file()),
        Err(error)
            if matches!(
                error.kind(),
                std::io::ErrorKind::NotFound | std::io::ErrorKind::NotADirectory
            ) =>
        {
            Ok(false)
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
        return Err(err(StatusCode::BAD_REQUEST, "no path"));
    }
    let path = std::path::PathBuf::from(crate::config::expand(path));
    let is_file = reader_is_file(&path)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "is_file": is_file }))
}

pub(crate) async fn read_file(
    State(_m): State<Mgr>,
    Query(q): Query<ReadReq>,
) -> ApiResult<wire::TextResult> {
    let path = q.path.trim();
    if path.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "no path"));
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

pub(crate) async fn highlight(
    State(m): State<Mgr>,
    Proto(q): Proto<wire::HighlightReq>,
) -> ApiResult<wire::TextResult> {
    let q: HighlightReq = domain(q)?;
    if q.text.len() as u64 > READ_LIMIT {
        return Err(err(
            StatusCode::BAD_REQUEST,
            "code is larger than the preview limit",
        ));
    }
    let command = m.config().await.commands.highlighter;
    let text = highlight_text(&command, &q.language, &q.text)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "text": text, "bytes": text.len() }))
}

async fn highlight_text(command: &str, language: &str, text: &str) -> anyhow::Result<String> {
    if command.trim().is_empty() {
        bail!("syntax highlighter is disabled");
    }

    let dir = std::path::PathBuf::from(crate::config::temp_dir("highlight"));
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
    tokio::fs::write(&path, text).await?;

    let result = run_highlighter(command, &path).await;
    let _ = tokio::fs::remove_file(&path).await;
    result
}

pub(crate) fn highlighter_argv(
    command: &str,
    path: &std::path::Path,
) -> anyhow::Result<Vec<String>> {
    let mut argv = crate::sandbox::shell_split(command);
    if argv.is_empty() {
        bail!("syntax highlighter is disabled");
    }
    let path = path
        .to_str()
        .ok_or_else(|| anyhow::anyhow!("highlight path is not valid UTF-8"))?;
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
    let mut process = Command::new(&argv[0]);
    process.args(&argv[1..]);
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
            anyhow::anyhow!("syntax highlighter timed out")
        } else {
            error.context("starting syntax highlighter")
        }
    })?;
    if output.stdout_truncated {
        bail!("syntax highlighter output exceeded the preview limit");
    }
    if !output.status.success() {
        bail!(
            "syntax highlighter failed: {}",
            String::from_utf8_lossy(&output.stderr).trim()
        );
    }
    String::from_utf8(output.stdout).context("syntax highlighter output is not valid UTF-8")
}

pub(crate) async fn read_image(
    State(_m): State<Mgr>,
    Query(q): Query<ReadReq>,
) -> ApiResult<wire::ImageResult> {
    let path = q.path.trim();
    if path.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "no path"));
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
        bail!("path is not a file");
    }
    let (unit, unit_size) = if label == "image" {
        ("MiB", 1024_u64 * 1024)
    } else {
        ("KiB", 1024_u64)
    };
    if metadata.len() > limit {
        bail!(
            "{label} is larger than the {} {} preview limit",
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
            "{label} grew beyond the {} {} preview limit",
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
    String::from_utf8(bytes).context("file is not valid UTF-8")
}

pub(crate) fn file_path(path: &str) -> anyhow::Result<std::path::PathBuf> {
    let path = crate::config::expand(path);
    if path.trim().is_empty() {
        bail!("no path");
    }

    let path = std::path::PathBuf::from(path);
    if !path.is_absolute() {
        bail!("path must be absolute");
    }
    Ok(path)
}

pub(crate) fn entry_name(name: &str) -> anyhow::Result<&str> {
    let name = name.trim();
    if name.is_empty() || name == "." || name == ".." {
        bail!("name is empty");
    }
    if name.contains('/') || name.contains('\\') || name.contains('\0') {
        bail!("name must be one path component");
    }
    Ok(name)
}

pub(crate) async fn create_file(Proto(q): Proto<wire::FileReq>) -> ApiResult<wire::Ack> {
    let q: FileReq = domain(q)?;
    let parent = file_path(&q.path).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let name = entry_name(&q.name).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let meta = tokio::fs::metadata(&parent)
        .await
        .with_context(|| format!("reading parent directory {}", parent.display()))
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    if !meta.is_dir() {
        return Err(err(
            StatusCode::BAD_REQUEST,
            format!("not a directory: {}", parent.display()),
        ));
    }

    let target = parent.join(name);
    if tokio::fs::symlink_metadata(&target).await.is_ok() {
        return Err(err(
            StatusCode::BAD_REQUEST,
            format!("already exists: {}", target.display()),
        ));
    }

    let result = if q.kind.trim() == "file" {
        tokio::fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&target)
            .await
            .map(|_| ())
            .with_context(|| format!("creating {}", target.display()))
    } else if q.kind.trim() == "folder" {
        tokio::fs::create_dir(&target)
            .await
            .with_context(|| format!("creating {}", target.display()))
    } else {
        return Err(err(StatusCode::BAD_REQUEST, "kind must be file or folder"));
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
        .ok_or_else(|| err(StatusCode::BAD_REQUEST, "cannot rename this path"))?;
    tokio::fs::symlink_metadata(&source)
        .await
        .with_context(|| format!("reading {}", source.display()))
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;

    let target = parent.join(name);
    if tokio::fs::symlink_metadata(&target).await.is_ok() {
        return Err(err(
            StatusCode::BAD_REQUEST,
            format!("already exists: {}", target.display()),
        ));
    }
    tokio::fs::rename(&source, &target)
        .await
        .with_context(|| format!("renaming {}", source.display()))
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "ok": true }))
}

pub(crate) async fn remove_file(Proto(q): Proto<wire::FileReq>) -> ApiResult<wire::Ack> {
    let q: FileReq = domain(q)?;
    let path = file_path(&q.path).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let meta = tokio::fs::symlink_metadata(&path)
        .await
        .with_context(|| format!("reading {}", path.display()))
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    if path.parent().is_none() {
        return Err(err(StatusCode::BAD_REQUEST, "cannot remove this path"));
    }

    if meta.is_dir() {
        tokio::fs::remove_dir_all(&path)
            .await
            .with_context(|| format!("removing {}", path.display()))
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    } else {
        tokio::fs::remove_file(&path)
            .await
            .with_context(|| format!("removing {}", path.display()))
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    }
    reply(json!({ "ok": true }))
}

pub(crate) const SEARCH_LIMIT: usize = 200;
// `rg --max-columns` controls its human output, but JSON match records still carry the whole
// line. A generated or minified asset can therefore turn one sidebar row into tens of thousands
// of glyphs. Keep the answer a preview, centred near the first match when there is room.
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
    out.push_str(&text[begin..end]);
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

/// Search one project without putting a pattern or path through a shell. `rg --json` keeps
/// filenames and matching text unambiguous; reading it a line at a time lets the endpoint
/// stop the process once the UI-sized answer is full rather than collecting an unbounded
/// repository search in memory.
pub(crate) async fn search(
    State(_m): State<Mgr>,
    Query(q): Query<SearchReq>,
) -> ApiResult<wire::SearchResult> {
    let _perf = crate::perf::timer("http-search");
    use tokio::io::{AsyncBufReadExt, BufReader};

    if q.path.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "no path"));
    }
    if q.q.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "no query"));
    }

    let dir = std::path::PathBuf::from(crate::config::expand(&q.path));
    let limit = q.limit.unwrap_or(SEARCH_LIMIT).clamp(1, SEARCH_LIMIT);

    let mut child = build_rg_command(&dir, &q)
        .spawn()
        .map_err(|e| err(StatusCode::BAD_REQUEST, format!("could not run rg: {e}")))?;
    let stdout = child.stdout.take().ok_or_else(|| {
        err(
            StatusCode::INTERNAL_SERVER_ERROR,
            "could not read rg output",
        )
    })?;
    let mut lines = BufReader::new(stdout).lines();
    let mut matches = Vec::new();
    let mut truncated = false;

    while let Some(line) = lines
        .next_line()
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?
    {
        let Ok(v) = serde_json::from_str::<Value>(&line) else {
            continue;
        };
        if v["type"] != "match" {
            continue;
        }
        if matches.len() >= limit {
            truncated = true;
            let _ = child.kill().await;
            break;
        }

        let data = &v["data"];
        let path = data["path"]["text"]
            .as_str()
            .unwrap_or("")
            .trim_start_matches("./");
        let text = data["lines"]["text"].as_str().unwrap_or("");
        let column = data["submatches"][0]["start"].as_u64().unwrap_or(0) as usize + 1;
        matches.push(json!({
            "path": path,
            "line": data["line_number"].as_u64().unwrap_or(0),
            "column": column,
            "text": search_preview(text, column),
        }));
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
                format!("rg exited with {status}"),
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

/// Returns a project's changed files for the git view; the game cannot run `git` inside agent
/// mount namespaces. Non-repositories return 200 with `repo: false`.
pub(crate) async fn git_status(
    State(_m): State<Mgr>,
    Query(q): Query<GitReq>,
) -> ApiResult<wire::GitResult> {
    let _perf = crate::perf::timer("http-git");
    if q.path.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "no path"));
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
mod reader_tests {
    use super::reader_is_file;

    #[tokio::test]
    async fn reader_metadata_distinguishes_missing_files_from_read_errors() {
        let dir = super::super::tests::fixture("reader-metadata");
        let file = dir.join("hidden.md");
        tokio::fs::write(&file, "hello").await.unwrap();
        assert!(reader_is_file(&file).await.unwrap());
        assert!(!reader_is_file(&dir).await.unwrap());
        assert!(!reader_is_file(&file.join("child")).await.unwrap());
        tokio::fs::remove_file(&file).await.unwrap();
        assert!(!reader_is_file(&file).await.unwrap());
        // A symlink loop is an I/O error, never evidence to dismiss a pinned tab.
        #[cfg(unix)]
        {
            std::os::unix::fs::symlink("loop", dir.join("loop")).unwrap();
            assert!(reader_is_file(&dir.join("loop")).await.is_err());
        }
        tokio::fs::remove_dir_all(&dir).await.unwrap();
    }
}
