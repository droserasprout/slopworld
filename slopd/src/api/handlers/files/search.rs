//! Search HTTP decoding, path resolution, and wire projection.
use super::super::{ApiResult, Mgr, err};
use crate::api::{
    protobuf::{Proto, path_text},
    types::SearchReq,
};
use crate::{files::search as service, shared::wire};
use axum::{
    extract::{Query, State},
    http::StatusCode,
};

impl From<SearchReq> for service::SearchOptions {
    fn from(q: SearchReq) -> Self {
        Self {
            q: q.q,
            include: q.include,
            exclude: q.exclude,
            regex: q.regex,
            case: q.case,
            word: q.word,
            hidden: q.hidden,
            gitignore: q.gitignore,
            limit: q.limit,
        }
    }
}
pub(crate) async fn search(
    State(_m): State<Mgr>,
    Query(q): Query<SearchReq>,
) -> ApiResult<wire::SearchResult> {
    let _perf = crate::perf::timer("http-search");
    if q.path.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "A path is required."));
    }
    let dir = std::path::PathBuf::from(crate::config::expand(&q.path));
    let out = service::search(&dir, &q.into()).await.map_err(|e| {
        let status = if e.is::<service::MissingOutput>() {
            StatusCode::INTERNAL_SERVER_ERROR
        } else {
            StatusCode::BAD_REQUEST
        };
        err(status, e)
    })?;
    crate::perf::count("http-search-matches", out.matches.len() as u64);
    Ok(Proto(wire::SearchResult {
        path: path_text(&dir)?,
        truncated: out.truncated,
        matches: out
            .matches
            .into_iter()
            .map(|row| wire::SearchMatch {
                path: row.path,
                line: row.line,
                column: row.column,
                text: row.text,
            })
            .collect(),
    }))
}
#[cfg(test)]
#[path = "search_tests.rs"]
mod tests;
