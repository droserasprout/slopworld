//! Syntax previews, theme discovery, and bounded highlighter execution.
//! Request-local styling preserves daemon defaults; files::preview owns raw file reads.
use anyhow::{Context, Result, bail};
use axum::{
    extract::{Query, State},
    http::StatusCode,
};

use super::files::READ_LIMIT;
use super::{ApiResult, Mgr, err};
use crate::api::types::HighlightReq;
use crate::{
    api::protobuf::{Proto, domain},
    shared::wire,
};
use std::{path::PathBuf, time::Duration};
use tokio::process::Command;

fn engine(argv: &[String]) -> &str {
    match argv
        .first()
        .and_then(|s| std::path::Path::new(s).file_name())
        .and_then(|s| s.to_str())
    {
        Some("highlight") => "highlight",
        Some("pygmentize") => "pygments",
        Some("bat") => "bat",
        _ => "",
    }
}

/// Parsed once, then passed through styling, file substitution, and execution.
struct HighlightCommand {
    argv: Vec<String>,
}

impl HighlightCommand {
    fn parse(command: &str) -> Result<Self> {
        Ok(Self {
            argv: crate::sandbox::shell_split(command)?,
        })
    }

    fn with_file(mut self, path: &std::path::Path) -> Result<Self> {
        if self.argv.is_empty() {
            bail!("The syntax highlighter is disabled.");
        }
        let path = path
            .to_str()
            .ok_or_else(|| anyhow::anyhow!("The temporary file path is not valid UTF-8."))?;
        let mut replaced = false;
        for arg in &mut self.argv {
            if arg.contains("%s") {
                *arg = arg.replace("%s", path);
                replaced = true;
            }
        }
        if !replaced {
            self.argv.push(path.to_owned());
        }
        Ok(self)
    }
}

fn themed_command(command: &str, requested_engine: &str, theme: &str) -> Result<HighlightCommand> {
    let mut command = HighlightCommand::parse(&crate::config::resolve_highlighter(command))?;
    if theme.is_empty() {
        return Ok(command);
    }
    let argv = &mut command.argv;
    let current = engine(argv);
    if current.is_empty() || current != requested_engine {
        bail!("The highlighter changed. Reload its appearance settings.");
    }
    // Theme names are values, never executable arguments or paths outside the theme catalog.
    if theme.len() > 160
        || !theme
            .chars()
            .all(|c| c.is_ascii_alphanumeric() || " -_./+()".contains(c))
        || theme.starts_with('/')
        || theme.split('/').any(|part| part == "..")
    {
        bail!("Invalid highlighter theme name.");
    }
    let option = match current {
        "highlight" => format!("--style={theme}"),
        "pygments" => format!("style={theme}"),
        "bat" => format!("--theme={theme}"),
        _ => bail!("The highlighter changed. Reload its appearance settings."),
    };
    let at = argv
        .iter()
        .position(|arg| arg == "--" || arg.contains("%s"))
        .unwrap_or(argv.len());
    let options = if current == "pygments" {
        vec!["-P".into(), option]
    } else {
        vec![option]
    };
    argv.splice(at..at, options);
    Ok(command)
}

#[derive(Default, serde::Deserialize)]
pub(crate) struct HighlightThemesQuery {
    pub command: Option<String>,
}

pub(crate) async fn highlight_themes(
    State(m): State<Mgr>,
    Query(q): Query<HighlightThemesQuery>,
) -> ApiResult<wire::HighlightThemes> {
    // The draft command is request-local, including an explicit empty (Off) choice.
    let command = q
        .command
        .unwrap_or(m.config().await.settings.commands.highlighter);
    let command = crate::config::resolve_highlighter(&command);
    let argv = crate::sandbox::shell_split(&command)
        .map_err(|error| err(StatusCode::BAD_REQUEST, error.to_string()))?;
    let kind = engine(&argv);
    if kind.is_empty() {
        return Ok(Proto(wire::HighlightThemes::default()));
    }
    let args: &[&str] = match kind {
        "highlight" => &["--list-scripts=themes"],
        "pygments" => &["-L", "styles", "--json"],
        "bat" => &["--list-themes", "--color=never"],
        _ => {
            return Err(err(
                StatusCode::BAD_REQUEST,
                "Unsupported syntax highlighter.",
            ));
        }
    };
    let Some(program) = argv.first() else {
        return Err(err(
            StatusCode::BAD_REQUEST,
            "The highlighter command is empty.",
        ));
    };
    let mut command = vec![program.clone()];
    command.extend(args.iter().map(|s| (*s).to_owned()));
    let output = run_highlighter_argv(&command)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let themes = parse_themes(kind, &output).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(wire::HighlightThemes {
        engine: kind.to_owned(),
        themes,
    }))
}

fn parse_themes(kind: &str, output: &str) -> Result<Vec<String>> {
    let mut themes: Vec<String> = match kind {
        "pygments" => {
            let value: serde_json::Value = serde_json::from_str(output)?;
            value
                .get("styles")
                .and_then(serde_json::Value::as_object)
                .ok_or_else(|| anyhow::anyhow!("Invalid Pygments theme catalog."))?
                .keys()
                .cloned()
                .collect()
        }
        "highlight" => output
            .lines()
            .filter_map(|line| line.split_once(" : "))
            .map(|(name, _)| name.trim().to_owned())
            .collect(),
        "bat" => output
            .lines()
            .map(str::trim)
            .filter(|s| !s.is_empty())
            .map(str::to_owned)
            .collect(),
        _ => Vec::new(),
    };
    themes.sort();
    themes.dedup();
    Ok(themes)
}

pub(crate) const HIGHLIGHT_LIMIT: usize = super::files::READ_LIMIT_BYTES * 4;
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
    let command = q
        .command
        .unwrap_or(m.config().await.settings.commands.highlighter);
    let command = themed_command(&command, &q.engine, &q.theme)
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let text = if command.argv.is_empty() {
        q.text
    } else {
        highlight_text(command, &q.language, &q.text)
            .await
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?
    };
    Ok(Proto(wire::TextResult {
        bytes: text.len() as u64,
        text,
        ..Default::default()
    }))
}

async fn highlight_text(
    command: HighlightCommand,
    language: &str,
    text: &str,
) -> anyhow::Result<String> {
    if command.argv.is_empty() {
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

    let command = command.with_file(&cleanup.0)?;
    let result = run_highlighter_argv(&command.argv).await;
    drop(cleanup);
    result
}

async fn run_highlighter_argv(argv: &[String]) -> anyhow::Result<String> {
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

#[cfg(test)]
#[path = "highlighting_tests.rs"]
mod tests;
