//! Git status HTTP projection; repository inspection and counts belong to crate::git.
use super::super::{ApiResult, App, err};
use crate::api::protobuf::Proto;
use crate::api::protobuf::path_text;
use crate::api::types::GitReq;
use crate::shared::wire;
use axum::{
    extract::{Query, State},
    http::StatusCode,
};

/// Return a project's changed files for the Git view.
/// The game cannot run `git` inside agent mount namespaces.
/// Paths outside repositories return 200 with `repo: false`.
pub(crate) async fn git_status(
    State(_m): State<App>,
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
        return Ok(Proto(wire::GitResult {
            path: path_text(&dir)?,
            ..Default::default()
        }));
    };
    crate::perf::count("http-git-rows", st.changes.len() as u64);

    Ok(Proto(wire::GitResult {
        repo: true,
        root: path_text(&st.root)?,
        branch: st.branch,
        changed: st.changed as u64,
        added: Some(st.added.into()),
        deleted: Some(st.deleted.into()),
        truncated: st.truncated,
        counts_complete: st.counts_complete,
        files: st
            .changes
            .into_iter()
            .map(|c| wire::GitFile {
                source: c.source,
                path: c.path,
                status: c.status,
                added: c.added.map(u64::from),
                deleted: c.deleted.map(u64::from),
            })
            .collect(),
        ..Default::default()
    }))
}

#[cfg(test)]
#[path = "git_tests.rs"]
mod tests;
