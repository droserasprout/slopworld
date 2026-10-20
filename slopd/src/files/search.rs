//! Bounded ripgrep execution, record decoding, and UTF-8 previews.
use anyhow::{Context, bail};
use base64::Engine;
use serde::Deserialize;
use tokio::io::{AsyncBufRead, AsyncBufReadExt};

#[derive(Debug, Default)]
pub(crate) struct SearchOptions {
    pub(crate) q: String,
    pub(crate) include: String,
    pub(crate) exclude: String,
    pub(crate) regex: bool,
    pub(crate) case: bool,
    pub(crate) word: bool,
    pub(crate) hidden: bool,
    pub(crate) gitignore: bool,
    pub(crate) limit: Option<usize>,
}
#[derive(Debug)]
pub(crate) struct SearchMatch {
    pub(crate) path: String,
    pub(crate) line: u64,
    pub(crate) column: u64,
    pub(crate) text: String,
}
#[derive(Debug)]
pub(crate) struct SearchResult {
    pub(crate) matches: Vec<SearchMatch>,
    pub(crate) truncated: bool,
}
#[derive(Debug)]
pub(crate) struct MissingOutput;
impl std::fmt::Display for MissingOutput {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str("The daemon could not read ripgrep output.")
    }
}
impl std::error::Error for MissingOutput {}

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

pub(crate) fn build_rg_command(
    path: &std::path::Path,
    q: &SearchOptions,
) -> tokio::process::Command {
    let mut cmd = tokio::process::Command::new("rg");
    cmd.current_dir(path)
        // UI request fields own search policy, independently of the daemon's environment.
        .arg("--no-config")
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

#[derive(Deserialize)]
#[serde(rename_all = "snake_case")]
enum RgText {
    Text(String),
    Bytes(String),
}

impl RgText {
    fn decode(self) -> anyhow::Result<Vec<u8>> {
        match self {
            Self::Text(text) => Ok(text.into_bytes()),
            Self::Bytes(bytes) => Ok(base64::engine::general_purpose::STANDARD.decode(bytes)?),
        }
    }

    fn path(self) -> anyhow::Result<String> {
        let path = String::from_utf8(self.decode()?).context(
            "Search encountered a filename that is not valid UTF-8. Rename it using a terminal.",
        )?;
        Ok(path.strip_prefix("./").unwrap_or(&path).to_owned())
    }
}

#[derive(Deserialize)]
#[serde(tag = "type", rename_all = "snake_case")]
enum RgEvent {
    Begin {
        data: RgBegin,
    },
    Match {
        data: RgMatch,
    },
    #[serde(other)]
    Other,
}

#[derive(Deserialize)]
struct RgBegin {
    path: RgText,
}

#[derive(Deserialize)]
struct RgMatch {
    path: RgText,
    lines: RgText,
    line_number: u64,
    submatches: Vec<RgSubmatch>,
}

#[derive(Deserialize)]
struct RgSubmatch {
    start: usize,
}

impl RgMatch {
    fn into_match(self) -> anyhow::Result<SearchMatch> {
        let path = self.path.path()?;
        let bytes = self.lines.decode()?;
        let start = self
            .submatches
            .first()
            .ok_or_else(|| anyhow::anyhow!("Ripgrep returned a match without a location."))?
            .start;
        let prefix = bytes
            .get(..start)
            .context("Ripgrep returned an invalid match location.")?;
        let text = String::from_utf8_lossy(&bytes);
        // Replacement characters change byte lengths. Center the preview using the
        // display offset, but keep the original source byte column on the wire.
        let display_start = String::from_utf8_lossy(prefix).len();
        Ok(SearchMatch {
            path,
            line: self.line_number,
            column: start.saturating_add(1) as u64,
            text: search_preview(&text, display_start.saturating_add(1)),
        })
    }
}

/// Decode events strictly: malformed matches must not become empty actionable paths.
fn decode_event(
    record: &[u8],
    include: Option<&globset::GlobSet>,
    included_file: &mut bool,
) -> anyhow::Result<Option<SearchMatch>> {
    match serde_json::from_slice::<RgEvent>(record)? {
        RgEvent::Begin { data } => {
            let path = data.path.path()?;
            *included_file = include.is_none_or(|filter| filter.is_match(path));
            Ok(None)
        }
        RgEvent::Match { data } if *included_file => data.into_match().map(Some),
        _ => Ok(None),
    }
}

/// Search one project without sending patterns or paths through a shell.
/// `rg --json` distinguishes filenames from matching text.
/// Bound each record while reading and stop at the UI or record limit.
/// This bounds the memory needed for repository search results.
pub(crate) async fn search(
    dir: &std::path::Path,
    q: &SearchOptions,
) -> anyhow::Result<SearchResult> {
    use tokio::io::BufReader;
    if q.q.is_empty() {
        bail!("A search query is required.");
    }
    let limit = q.limit.unwrap_or(SEARCH_LIMIT).clamp(1, SEARCH_LIMIT);
    let include = include_filter(&q.include)?;

    let mut child = build_rg_command(dir, q)
        .spawn()
        .map_err(|error| anyhow::anyhow!("The daemon could not start ripgrep: {error}"))?;
    let Some(stdout) = child.stdout.take() else {
        stop_search_child(&mut child).await;
        return Err(MissingOutput.into());
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
                return Err(error.into());
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
                    return Err(error.into());
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
        let matched = match decode_event(&record, include.as_ref(), &mut included_file) {
            Ok(Some(matched)) => matched,
            Ok(None) => continue,
            Err(error) => {
                stop_search_child(&mut child).await;
                return Err(error);
            }
        };
        if matches.len() >= limit {
            truncated = true;
            stop_search_child(&mut child).await;
            break;
        }

        matches.push(matched);
    }

    if !truncated {
        let status = child.wait().await?;
        // ripgrep uses 1 for a clean search with no matches.
        if !status.success() && status.code() != Some(1) {
            bail!("Ripgrep exited with status {status}.");
        }
    }

    Ok(SearchResult { matches, truncated })
}

#[cfg(test)]
#[path = "search_tests.rs"]
mod tests;
