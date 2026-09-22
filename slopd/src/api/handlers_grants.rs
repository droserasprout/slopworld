use crate::api::protobuf::{domain, reply, Proto};
use crate::shared::wire;
use axum::extract::{Path, State};
use axum::http::StatusCode;

use serde_json::json;

use crate::grant::Level;

use super::super::types::GrantReq;
use super::{err, ApiResult, Mgr};

/// Mint a grant: let `grantor` watch or drive the named `sessions`. Root-only by the router,
/// so only the mod asks. The daemon refuses a host session in the scope - the one line the
/// whole scheme is for - and refuses a grantor or target that is not there. The token comes
/// back once; the daemon keeps a private copy so the credential survives daemon restarts.
pub(crate) async fn mint_grant(
    State(m): State<Mgr>,
    Proto(q): Proto<wire::GrantReq>,
) -> ApiResult<wire::GrantResult> {
    let q: GrantReq = domain(q)?;
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
    reply(json!({ "ok": true, "token": token }))
}

/// How many grants are live, for the mod's readout. Not the tokens themselves: those are
/// bearer secrets and leave the daemon once, at the mint.
pub(crate) async fn list_grants(State(m): State<Mgr>) -> ApiResult<wire::GrantsReply> {
    reply(json!({ "grants": m.grant_count().await }))
}

/// Drop every grant a session minted, by hand rather than by its exit. The mod's revoke.
pub(crate) async fn revoke_grants(
    State(m): State<Mgr>,
    Path(grantor): Path<String>,
) -> ApiResult<wire::Ack> {
    m.revoke_grants(&grantor)
        .await
        .map_err(|error| err(StatusCode::INTERNAL_SERVER_ERROR, error))?;
    reply(json!({ "ok": true }))
}
