//! Bounded ripgrep records, UTF-8 previews, and search process cleanup.
use super::super::{ApiResult, Mgr, err};
use crate::api::protobuf::Proto;
use crate::api::protobuf::path_text;
use crate::api::types::SearchReq;
use crate::shared::wire;
use axum::{
    extract::{Query, State},
    http::StatusCode,
};
use serde_json::Value;
use tokio::io::{AsyncBufRead, AsyncBufReadExt};

/// Default and maximum matching lines returned per checkout.
pub(crate) const SEARCH_LIMIT: usize = 1_000;
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
    // Only negative globs belong here: positive globs override ignore and hidden
    // policies. Inclusion is applied to returned paths before the result limit.
    add_exclusion(&mut cmd, &q.exclude);
    cmd.arg("--glob=!.git/**")
        .arg("--")
        .arg(&q.q)
        .arg(".")
        .stdout(std::process::Stdio::piped())
        .stderr(std::process::Stdio::null())
        .kill_on_drop(true);
    cmd
}

fn add_exclusion(cmd: &mut tokio::process::Command, pattern: &str) {
    let pattern = pattern.trim_end_matches('/');
    if pattern.is_empty() {
        return;
    }
    cmd.arg("--glob").arg(format!("!{pattern}"));
    if !pattern.contains(['*', '?', '[', '{', '\\']) {
        let anywhere = if pattern.contains('/') { "" } else { "**/" };
        cmd.arg("--glob").arg(format!("!{anywhere}{pattern}/**"));
    }
}

fn include_filter(pattern: &str) -> Result<Option<globset::GlobSet>, globset::Error> {
    let pattern = pattern.trim().trim_end_matches('/');
    if pattern.is_empty() {
        return Ok(None);
    }
    let anywhere = if pattern.contains('/') { "" } else { "**/" };
    let mut patterns = vec![format!("{anywhere}{}", pattern.trim_start_matches('/'))];
    if !pattern.contains(['*', '?', '[', '{', '\\']) {
        patterns.push(format!("{anywhere}{}/**", pattern.trim_start_matches('/')));
    }
    let mut builder = globset::GlobSetBuilder::new();
    for pattern in patterns {
        builder.add(
            globset::GlobBuilder::new(&pattern)
                .literal_separator(true)
                .build()?,
        );
    }
    builder.build().map(Some)
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

/// Drain an oversized record from a file outside the include filter without
/// allocating its matching line or reporting the selected results as truncated.
async fn discard_search_record<R: AsyncBufRead + Unpin>(reader: &mut R) -> std::io::Result<()> {
    loop {
        let available = reader.fill_buf().await?;
        let newline = available.iter().position(|byte| *byte == b'\n');
        let count = newline.map_or(available.len(), |index| index + 1);
        reader.consume(count);
        if count == 0 || newline.is_some() {
            return Ok(());
        }
    }
}

fn search_match(record: &Value) -> wire::SearchMatch {
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
    wire::SearchMatch {
        path: path.to_owned(),
        line: data
            .and_then(|data| data.get("line_number"))
            .and_then(Value::as_u64)
            .unwrap_or(0),
        column: column as u64,
        text: search_preview(text, column),
    }
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
    let include = include_filter(&q.include).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;

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
    let mut included_file = true;

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
            SearchRecord::TooLarge if !included_file => {
                if let Err(error) = discard_search_record(&mut reader).await {
                    stop_search_child(&mut child).await;
                    return Err(err(StatusCode::BAD_REQUEST, error));
                }
                continue;
            }
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
        let kind = v.get("type").and_then(Value::as_str);
        if kind == Some("begin") {
            included_file = include
                .as_ref()
                .is_none_or(|filter| filter.is_match(search_match(&v).path));
        }
        if kind != Some("match") || !included_file {
            continue;
        }
        let matched = search_match(&v);
        if matches.len() >= limit {
            truncated = true;
            stop_search_child(&mut child).await;
            break;
        }

        matches.push(matched);
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

    Ok(Proto(wire::SearchResult {
        path: path_text(&dir)?,
        matches,
        truncated,
    }))
}

#[cfg(test)]
#[path = "search_tests.rs"]
mod tests;
