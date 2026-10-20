//! Directory listing HTTP projection.
use super::super::{ApiResult, App, err};
use crate::api::{
    protobuf::{Proto, path_text},
    types::BrowseReq,
};
use crate::files::browse::{browse_limit, list_dir};
use crate::shared::wire;
use axum::{
    extract::{Query, State},
    http::StatusCode,
};

/// Supply directory listings for project selection and the Files tree without direct host filesystem access from the mod.
/// Always include directories. Include files only when requested.
pub(crate) async fn browse(
    State(_m): State<App>,
    Query(q): Query<BrowseReq>,
) -> ApiResult<wire::BrowseResult> {
    let _perf = crate::perf::timer("http-browse");
    let base = if q.path.is_empty() {
        dirs::home_dir().unwrap_or_else(|| "/".into())
    } else {
        std::path::PathBuf::from(crate::config::expand(&q.path))
    };

    let limit = browse_limit(q.limit);
    let out = list_dir(&base, q.files, q.hidden, limit, &q.filter, q.gitignore)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;

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
