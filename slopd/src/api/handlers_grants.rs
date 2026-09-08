use axum::extract::{Path, State};
use axum::http::StatusCode;
use axum::Json;
use serde_json::json;

use crate::grant::Level;

use super::super::types::GrantReq;
use super::{err, ApiResult, Mgr};

/// Mint a grant: let `grantor` watch or drive the named `sessions`. Root-only by the router,
/// so only the mod asks. The daemon refuses a host session in the scope - the one line the
/// whole scheme is for - and refuses a grantor or target that is not there. The token comes
/// back once and is never stored on the file; losing it means minting another.
pub(crate) async fn mint_grant(State(m): State<Mgr>, Json(q): Json<GrantReq>) -> ApiResult {
    let level = match q.level.trim() {
        "ro" => Level::Ro,
        "rw" => Level::Rw,
        other => {
            return Err(err(
                StatusCode::BAD_REQUEST,
                format!("level must be \"ro\" or \"rw\", not {other:?}"),
            ))
        }
    };
    let token = m
        .mint_grant(q.grantor, q.sessions, level)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true, "token": token })))
}

/// How many grants are live, for the mod's readout. Not the tokens themselves: those are
/// bearer secrets and leave the daemon once, at the mint.
pub(crate) async fn list_grants(State(m): State<Mgr>) -> ApiResult {
    Ok(Json(json!({ "grants": m.grant_count().await })))
}

/// Drop every grant a session minted, by hand rather than by its exit. The mod's revoke.
pub(crate) async fn revoke_grants(State(m): State<Mgr>, Path(grantor): Path<String>) -> ApiResult {
    m.revoke_grants(&grantor).await;
    Ok(Json(json!({ "ok": true })))
}
