//! Configuration HTTP boundaries.
use crate::api::protobuf::{domain, reply, Proto};
use crate::shared::wire;

use axum::extract::State;
use axum::http::StatusCode;

use serde_json::json;

use super::super::types::*;
use super::{err, ok_json, ApiResult, Mgr};

pub(crate) async fn get_config(State(m): State<Mgr>) -> ApiResult<wire::ConfigResult> {
    // Keep the raw text and parsed values from different snapshots when a user edits the file
    // outside the daemon.
    m.reload_if_changed().await;
    let text = std::fs::read_to_string(&m.cfg_path)
        .map_err(|e| err(StatusCode::INTERNAL_SERVER_ERROR, e))?;
    // The token never leaves the daemon as written: the raw text and the parsed values both
    // carry the sentinel, and a write that sends it back is read as "unchanged". The endpoint
    // is behind the token itself, so this guards the one case that is not - the raw editor,
    // and any future client that reaches the config without holding the secret first.
    let cfg = m.config().await;
    let factory = crate::config::Config::default();
    let caps = crate::runtime::capabilities();
    reply(json!({
        "path": m.cfg_path,
        "text": crate::config::redact_token_text(&text),
        "values": cfg.redacted(),
        "metadata": {
            // Factory defaults are deliberately a response-only read model. They are not
            // accepted as a client patch and contain no secrets.
            "defaults": factory,
            "usage_catalog": crate::usage::catalog(),
            "temporary_root": crate::config::TEMP_ROOT,
            "terminal": caps.terminal,
        },
    }))
}

pub(crate) async fn capabilities() -> ApiResult<wire::Capabilities> {
    reply(json!(crate::runtime::capabilities()))
}

pub(crate) async fn put_config(
    State(m): State<Mgr>,
    Proto(req): Proto<wire::ReplaceConfigRequest>,
) -> ApiResult<wire::Ack> {
    let req: ConfigReq = domain(req)?;
    ok_json(m.replace_config(&req.text).await)
}

/// Apply only the fields named by the client, leaving unmentioned fields untouched.
pub(crate) async fn put_config_patch(
    State(m): State<Mgr>,
    Proto(req): Proto<wire::ConfigPatch>,
) -> ApiResult<wire::Ack> {
    ok_json(
        m.patch_config(super::super::protobuf::config_patch(req)?)
            .await,
    )
}
