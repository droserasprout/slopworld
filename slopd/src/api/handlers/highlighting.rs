//! Installed theme discovery and request-local styling; daemon defaults remain unchanged.
use anyhow::{Result, bail};
use axum::{
    extract::{Query, State},
    http::StatusCode,
};
use serde_json::json;

use super::{ApiResult, Mgr, err};
use crate::{api::protobuf::reply, shared::wire};

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

pub(super) fn themed_command(command: &str, requested_engine: &str, theme: &str) -> Result<String> {
    if theme.is_empty() {
        return Ok(command.to_owned());
    }
    let mut argv = crate::sandbox::shell_split(command);
    let current = engine(&argv);
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
    Ok(argv
        .iter()
        .map(|arg| format!("'{}'", arg.replace('\'', "'\\''")))
        .collect::<Vec<_>>()
        .join(" "))
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
    let command = q.command.unwrap_or(m.config().await.commands.highlighter);
    let argv = crate::sandbox::shell_split(&command);
    let kind = engine(&argv);
    if kind.is_empty() {
        return reply(json!({ "engine": "", "themes": [] }));
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
    let output = super::files::run_highlighter_argv(&command)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let themes = parse_themes(kind, &output).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "engine": kind, "themes": themes }))
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

#[cfg(test)]
#[path = "highlighting_tests.rs"]
mod tests;
