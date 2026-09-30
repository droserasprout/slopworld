mod client_message;
mod handlers;
pub(crate) mod protobuf;
mod router;
mod types;
mod ws;

pub(crate) use crate::shared::protocol::TOKEN_HEADER;
pub(crate) use router::router;

use std::sync::Arc;

use crate::shared::wire;
use axum::http::{HeaderMap, StatusCode};
use protobuf::{ApiError, Proto};
use serde::de::DeserializeOwned;
use serde_json::Value;
#[cfg(test)]
use serde_json::json;

use crate::session::Manager;

pub(super) type Mgr = Arc<Manager>;
pub(super) type ApiResult<T = wire::Ack> = Result<Proto<T>, ApiError>;

/// Requests permit only listed fields. Disk documents can preserve unrelated extensions.
fn parse_owned<T: DeserializeOwned>(value: Value, fields: &[&str]) -> Result<T, ApiError> {
    if let Some(object) = value.as_object()
        && let Some(field) = object
            .keys()
            .find(|field| !fields.contains(&field.as_str()))
    {
        return Err(err(
            StatusCode::BAD_REQUEST,
            format!("unknown request field {field:?}"),
        ));
    }
    serde_json::from_value(value).map_err(|error| err(StatusCode::BAD_REQUEST, error))
}

pub(super) fn parse_session(value: Value) -> Result<crate::config::SessionCfg, ApiError> {
    parse_owned(
        value,
        &[
            "name",
            "label",
            "state_id",
            "worktree",
            "project",
            "command",
            "cmd",
            "args",
            "command_snapshot",
            "sandbox",
            "sandbox_snapshots",
            "persistent_tmp",
            "network",
            "dns",
            "limits",
            "autostart",
            "auto_resume",
            "worker",
            "parent",
            "task_id",
        ],
    )
}

pub(super) fn parse_project(value: Value) -> Result<crate::config::ProjectCfg, ApiError> {
    parse_owned(
        value,
        &["name", "dir", "temp", "mounts", "id", "worktree_root"],
    )
}

pub(super) fn parse_template(value: Value) -> Result<crate::session::AgentTemplate, ApiError> {
    serde_json::from_value(value).map_err(|error| err(StatusCode::BAD_REQUEST, error))
}

pub(super) fn err(code: StatusCode, e: impl std::fmt::Display) -> ApiError {
    (
        code,
        Proto(wire::Error {
            error: e.to_string(),
        }),
    )
}

/// Read the request token, if present. `Manager::resolve_cap` checks it against root credentials and live grants.
/// The `/ws` upgrade also uses this helper because the client sends the header only with the upgrade request.
pub(crate) fn presented_token(headers: &HeaderMap) -> Option<String> {
    headers
        .get(TOKEN_HEADER)
        .and_then(|v| v.to_str().ok())
        .map(str::to_string)
}

#[cfg(test)]
#[path = "tests.rs"]
mod tests;

#[cfg(test)]
#[path = "request_tests.rs"]
mod request_tests;
