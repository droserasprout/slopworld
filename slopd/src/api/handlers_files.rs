//! Files, preview, search, and Git HTTP boundaries.

use anyhow::{bail, Context};
use axum::extract::{Query, State};
use axum::http::StatusCode;
use axum::Json;
use base64::{engine::general_purpose::STANDARD as BASE64, Engine as _};
use serde_json::{json, Value};
use std::process::Stdio;
use std::time::Duration;
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

pub(crate) fn browse_limit(requested: Option<usize>) -> usize {
    requested.unwrap_or(BROWSE_LIMIT).clamp(1, BROWSE_LIMIT)
}

/// What one directory holds, as far as this endpoint is concerned. Split out from the
/// handler so the rules below can be tested against a real directory without standing a
/// manager and a router up around them.
pub(crate) struct Listing {
    pub(crate) dirs: Vec<String>,
    pub(crate) files: Vec<String>,
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
        files: Vec::new(),
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
            out.dirs.push(name);
        } else {
            out.files.push(name);
        }
    }

    out.dirs.sort();
    out.files.sort();
    Ok(out)
}

/// Filter entries that `.gitignore` rules cover. Runs `git check-ignore` in the listed
/// directory; silently skips filtering when git is absent or the path is outside a repo.
async fn filter_gitignored(base: &std::path::Path, listing: &mut Listing) {
    use tokio::io::AsyncWriteExt;

    if listing.dirs.is_empty() && listing.files.is_empty() {
        return;
    }

    let mut cmd = tokio::process::Command::new("git");
    cmd.current_dir(base)
        .args(["check-ignore", "--stdin", "-z"])
        .stdin(std::process::Stdio::piped())
        .stdout(std::process::Stdio::piped())
        .stderr(std::process::Stdio::null());

    let mut child = match cmd.spawn() {
        Ok(c) => c,
        Err(_) => return,
    };

    {
        let mut stdin = match child.stdin.take() {
            Some(s) => s,
            None => return,
        };
        let mut input = Vec::new();
        for name in listing.dirs.iter().chain(listing.files.iter()) {
            input.extend_from_slice(name.as_bytes());
            input.push(0);
        }
        let _ = stdin.write_all(&input).await;
    }

    let output = match child.wait_with_output().await {
        Ok(o) => o,
        Err(_) => return,
    };

    let mut ignored = std::collections::HashSet::new();
    for chunk in output.stdout.split(|&b| b == 0) {
        if let Ok(s) = std::str::from_utf8(chunk) {
            if !s.is_empty() {
                ignored.insert(s.to_owned());
            }
        }
    }

    listing.dirs.retain(|n| !ignored.contains(n));
    listing.files.retain(|n| !ignored.contains(n));
}

/// So the dialog can pick a project dir, and the files view can draw a tree, without
/// the mod touching the host filesystem itself. `dirs` is what it always was; `files`
/// is asked for.
pub(crate) async fn browse(State(_m): State<Mgr>, Query(q): Query<BrowseReq>) -> ApiResult {
    let base = if q.path.is_empty() {
        dirs::home_dir().unwrap_or_else(|| "/".into())
    } else {
        std::path::PathBuf::from(crate::config::expand(&q.path))
    };

    let limit = browse_limit(q.limit);
    let mut out = list_dir(&base, q.files, q.hidden, limit)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;

    if q.gitignore {
        filter_gitignored(&base, &mut out).await;
    }

    Ok(Json(json!({
        "path": base,
        "parent": base.parent(),
        "dirs": out.dirs,
        "files": out.files,
        "truncated": out.truncated,
    })))
}

pub(crate) async fn read_file(State(_m): State<Mgr>, Query(q): Query<ReadReq>) -> ApiResult {
    let path = q.path.trim();
    if path.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "no path"));
    }

    let path = std::path::PathBuf::from(crate::config::expand(path));
    let text = read_preview(&path)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({
        "path": path,
        "text": text,
        "bytes": text.len(),
    })))
}

pub(crate) const HIGHLIGHT_LIMIT: usize = READ_LIMIT as usize * 4;
pub(crate) const HIGHLIGHT_TIMEOUT: Duration = Duration::from_secs(3);

pub(crate) async fn highlight(State(m): State<Mgr>, Json(q): Json<HighlightReq>) -> ApiResult {
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
    Ok(Json(json!({ "text": text, "bytes": text.len() })))
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
    let mut child = Command::new(&argv[0])
        .args(&argv[1..])
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .kill_on_drop(true)
        .spawn()
        .context("starting syntax highlighter")?;
    let stdout = child
        .stdout
        .take()
        .context("capturing highlighter stdout")?;
    let stderr = child
        .stderr
        .take()
        .context("capturing highlighter stderr")?;

    let collected = tokio::time::timeout(HIGHLIGHT_TIMEOUT, async {
        let out = read_highlight_output(stdout, HIGHLIGHT_LIMIT);
        let err = read_highlight_output(stderr, 16 * 1024);
        let status = child.wait();
        let (out, err, status) = tokio::join!(out, err, status);
        Ok::<_, anyhow::Error>((out?, err?, status?))
    })
    .await;
    let (stdout, stderr, status) = match collected {
        Ok(result) => result?,
        Err(_) => {
            let _ = child.kill().await;
            let _ = child.wait().await;
            bail!("syntax highlighter timed out");
        }
    };
    if stdout.1 {
        bail!("syntax highlighter output exceeded the preview limit");
    }
    if !status.success() {
        bail!(
            "syntax highlighter failed: {}",
            String::from_utf8_lossy(&stderr.0).trim()
        );
    }
    String::from_utf8(stdout.0).context("syntax highlighter output is not valid UTF-8")
}

pub(crate) async fn read_highlight_output<R: tokio::io::AsyncRead + Unpin>(
    reader: R,
    limit: usize,
) -> anyhow::Result<(Vec<u8>, bool)> {
    let mut bytes = Vec::new();
    reader
        .take(limit as u64 + 1)
        .read_to_end(&mut bytes)
        .await?;
    let truncated = bytes.len() > limit;
    if truncated {
        bytes.truncate(limit);
    }
    Ok((bytes, truncated))
}

pub(crate) async fn read_image(State(_m): State<Mgr>, Query(q): Query<ReadReq>) -> ApiResult {
    let path = q.path.trim();
    if path.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "no path"));
    }

    let path = std::path::PathBuf::from(crate::config::expand(path));
    let bytes = read_image_bytes(&path)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({
        "path": path,
        "data": BASE64.encode(&bytes),
        "bytes": bytes.len(),
    })))
}

pub(crate) async fn read_image_bytes(path: &std::path::Path) -> anyhow::Result<Vec<u8>> {
    let metadata = tokio::fs::metadata(path).await?;
    if !metadata.is_file() {
        bail!("path is not a file");
    }
    if metadata.len() > IMAGE_LIMIT {
        bail!(
            "image is larger than the {} MiB preview limit",
            IMAGE_LIMIT / (1024 * 1024)
        );
    }

    let bytes = tokio::fs::read(path).await?;
    if bytes.len() as u64 > IMAGE_LIMIT {
        bail!(
            "image grew beyond the {} MiB preview limit",
            IMAGE_LIMIT / (1024 * 1024)
        );
    }
    Ok(bytes)
}

pub(crate) async fn read_preview(path: &std::path::Path) -> anyhow::Result<String> {
    let metadata = tokio::fs::metadata(path).await?;
    if !metadata.is_file() {
        bail!("path is not a file");
    }
    if metadata.len() > READ_LIMIT {
        bail!(
            "file is larger than the {} KiB preview limit",
            READ_LIMIT / 1024
        );
    }

    let mut file = tokio::fs::File::open(path).await?;
    let mut bytes = Vec::with_capacity(metadata.len() as usize);
    file.read_to_end(&mut bytes).await?;
    if bytes.len() as u64 > READ_LIMIT {
        bail!(
            "file grew beyond the {} KiB preview limit",
            READ_LIMIT / 1024
        );
    }

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

pub(crate) async fn create_file(Json(q): Json<FileReq>) -> ApiResult {
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

    Ok(Json(json!({ "ok": true })))
}

pub(crate) async fn rename_file(Json(q): Json<FileReq>) -> ApiResult {
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
    Ok(Json(json!({ "ok": true })))
}

pub(crate) async fn remove_file(Json(q): Json<FileReq>) -> ApiResult {
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
    Ok(Json(json!({ "ok": true })))
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
pub(crate) async fn search(State(_m): State<Mgr>, Query(q): Query<SearchReq>) -> ApiResult {
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

    Ok(Json(json!({
        "path": dir,
        "matches": matches,
        "truncated": truncated,
    })))
}

/// Returns a project's changed files for the git view; the game cannot run `git` inside agent
/// mount namespaces. Non-repositories return 200 with `repo: false`.
pub(crate) async fn git_status(State(_m): State<Mgr>, Query(q): Query<GitReq>) -> ApiResult {
    if q.path.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "no path"));
    }
    let dir = std::path::PathBuf::from(crate::config::expand(&q.path));

    let out = crate::git::status(&dir)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;

    let Some(st) = out else {
        return Ok(Json(json!({ "repo": false, "path": dir })));
    };

    Ok(Json(json!({
        "repo": true,
        "root": st.root,
        "branch": st.branch,
        "changed": st.changed,
        "added": st.added,
        "deleted": st.deleted,
        "truncated": st.truncated,
        "files": st.changes.iter().map(|c| json!({
            "path": c.path,
            "status": c.status,
            "added": c.added,
            "deleted": c.deleted,
        })).collect::<Vec<_>>(),
    })))
}
