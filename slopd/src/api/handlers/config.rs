//! Configuration HTTP boundaries.
use crate::api::protobuf::{ApiError, Proto, domain, reply};
use crate::shared::wire;

use axum::extract::State;
use axum::http::StatusCode;

use serde_json::json;

use super::super::types::*;
use super::{ApiResult, Mgr, err, ok_json};

pub(crate) async fn get_config(State(m): State<Mgr>) -> ApiResult<wire::ConfigResult> {
    // Keep the raw text and parsed values from different snapshots when a user edits the file
    // outside the daemon.
    m.reload_if_changed().await;
    let text = std::fs::read_to_string(&m.cfg_path)
        .map_err(|e| err(StatusCode::INTERNAL_SERVER_ERROR, e))?;
    // Replace the token with the redaction sentinel in raw text and parsed values.
    // Writing the sentinel preserves the token. Authentication still protects this endpoint.
    // Redaction prevents the raw editor and other configuration readers from displaying the secret.
    let cfg = m.config().await;
    let redacted_text = crate::config::redact_token_text(&text)
        .map_err(|e| err(StatusCode::INTERNAL_SERVER_ERROR, e))?;
    let factory = crate::config::Config::default();
    let caps = crate::runtime::capabilities();
    reply(json!({
        "path": m.cfg_path,
        "text": redacted_text,
        "values": cfg.redacted(),
        "metadata": {
            // Factory defaults are deliberately a response-only read model. They are not
            // accepted as a client patch and contain no secrets.
            "defaults": factory,
            "usage_catalog": crate::usage::catalog(),
            "temporary_root": crate::paths::TEMP_ROOT,
            "terminal": caps.terminal,
        },
    }))
}

pub(crate) async fn capabilities() -> ApiResult<wire::Capabilities> {
    reply(json!(crate::runtime::capabilities()))
}

pub(crate) async fn whereis() -> ApiResult<wire::WhereIsReply> {
    reply(json!({ "binaries": crate::runtime::whereis() }))
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
    ok_json(m.patch_config(config_patch(req)?).await)
}

/// Assemble only selected editable leaves. Decode escaped map keys before looking up source values.
pub(crate) fn config_patch(patch: wire::ConfigPatch) -> Result<serde_json::Value, ApiError> {
    let values = serde_json::to_value(patch.values.unwrap_or_default())
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let mut result = serde_json::json!({});
    for path in patch.paths {
        let parts: Vec<_> = path
            .split('.')
            .map(|p| p.replace("~1", ".").replace("~0", "~"))
            .collect();
        let root = parts.first().map(String::as_str);
        let first_key = parts.get(1).map(String::as_str);
        if parts.len() < 2
            || !matches!(root, Some("daemon" | "defaults" | "commands"))
            || (root == Some("daemon") && matches!(first_key, Some("bind" | "token")))
        {
            return Err(err(
                StatusCode::BAD_REQUEST,
                "This config path is not valid for editing.",
            ));
        }
        let mut source = &values;
        for part in &parts {
            source = source.get(part).ok_or_else(|| {
                err(
                    StatusCode::BAD_REQUEST,
                    format!("unknown config path: {path}"),
                )
            })?;
        }
        if source.is_object() {
            return Err(err(
                StatusCode::BAD_REQUEST,
                "Choose a config path that identifies one value.",
            ));
        }
        #[expect(
            clippy::expect_used,
            reason = "the path length was validated before splitting its final component"
        )]
        let (leaf, parents) = parts.split_last().expect("validated path length");
        let mut target = &mut result;
        for part in parents {
            target = target
                .as_object_mut()
                .ok_or_else(|| err(StatusCode::BAD_REQUEST, "Config paths must not overlap."))?
                .entry(part.clone())
                .or_insert_with(|| serde_json::json!({}));
        }
        target
            .as_object_mut()
            .ok_or_else(|| err(StatusCode::BAD_REQUEST, "Config paths must not overlap."))?
            .insert(leaf.clone(), source.clone());
    }
    Ok(result)
}
