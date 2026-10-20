//! Syntax preview HTTP boundary; files::highlighting owns tool execution and styling.
use super::{ApiResult, App, err};
use crate::api::{
    protobuf::{Proto, domain},
    types::HighlightReq,
};
use crate::{files::highlighting as service, shared::wire};
use axum::{
    extract::{Query, State},
    http::StatusCode,
};

#[derive(Default, serde::Deserialize)]
pub(crate) struct HighlightThemesQuery {
    pub command: Option<String>,
}

pub(crate) async fn highlight_themes(
    State(m): State<App>,
    Query(q): Query<HighlightThemesQuery>,
) -> ApiResult<wire::HighlightThemes> {
    let command = q.command.unwrap_or(
        m.config_authority
            .read_config()
            .await
            .clone()
            .settings
            .commands
            .highlighter,
    );
    let catalog = service::themes(&command)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(wire::HighlightThemes {
        engine: catalog.engine,
        themes: catalog.themes,
    }))
}

pub(crate) async fn highlight(
    State(m): State<App>,
    Proto(q): Proto<wire::HighlightReq>,
) -> ApiResult<wire::TextResult> {
    let q: HighlightReq = domain(q)?;
    let command = q.command.unwrap_or(
        m.config_authority
            .read_config()
            .await
            .clone()
            .settings
            .commands
            .highlighter,
    );
    let text = service::highlight(&command, &q.engine, &q.theme, &q.language, &q.text)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(wire::TextResult {
        bytes: text.len() as u64,
        text,
        ..Default::default()
    }))
}
#[cfg(test)]
#[path = "highlighting_tests.rs"]
mod tests;
