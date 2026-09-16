mod handlers;
mod router;
mod types;
mod ws;

pub(crate) use crate::shared::protocol::TOKEN_HEADER;
pub(crate) use router::router;

use std::sync::Arc;

use axum::http::{HeaderMap, StatusCode};
use axum::Json;
use serde::de::DeserializeOwned;
use serde_json::json;
use serde_json::Value;

use crate::session::Manager;

pub(super) type Mgr = Arc<Manager>;
pub(super) type ApiResult = Result<Json<serde_json::Value>, (StatusCode, Json<serde_json::Value>)>;

/// Request fields are an explicit allowlist; disk documents can retain unrelated extensions.
fn parse_owned<T: DeserializeOwned>(
    value: Value,
    fields: &[&str],
) -> Result<T, (StatusCode, Json<Value>)> {
    if let Some(object) = value.as_object() {
        if let Some(field) = object
            .keys()
            .find(|field| !fields.contains(&field.as_str()))
        {
            return Err(err(
                StatusCode::BAD_REQUEST,
                format!("unknown request field {field:?}"),
            ));
        }
    }
    serde_json::from_value(value).map_err(|error| err(StatusCode::BAD_REQUEST, error))
}

pub(super) fn parse_session(
    value: Value,
) -> Result<crate::config::SessionCfg, (StatusCode, Json<Value>)> {
    parse_owned(
        value,
        &[
            "name",
            "label",
            "state_id",
            "project",
            "command",
            "cmd",
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

pub(super) fn parse_project(
    value: Value,
) -> Result<crate::config::ProjectCfg, (StatusCode, Json<Value>)> {
    parse_owned(value, &["name", "dir", "temp", "mounts"])
}

pub(super) fn parse_template(
    value: Value,
) -> Result<crate::session::AgentTemplate, (StatusCode, Json<Value>)> {
    serde_json::from_value(value).map_err(|error| err(StatusCode::BAD_REQUEST, error))
}

pub(super) fn err(
    code: StatusCode,
    e: impl std::fmt::Display,
) -> (StatusCode, Json<serde_json::Value>) {
    (code, Json(json!({ "error": e.to_string() })))
}

/// The token a request presents, if any. What `Manager::resolve_cap` weighs against the root
/// and the live grants. Shared with the `/ws` upgrade, which reads it itself because the header
/// rides only on the upgrade request.
pub(crate) fn presented_token(headers: &HeaderMap) -> Option<String> {
    headers
        .get(TOKEN_HEADER)
        .and_then(|v| v.to_str().ok())
        .map(str::to_string)
}

#[cfg(test)]
mod tests {
    use super::*;
    use axum::http::HeaderValue;

    #[test]
    fn errors_keep_the_status_and_put_the_message_in_json() {
        let (status, Json(body)) = err(StatusCode::BAD_REQUEST, "bad request body");
        assert_eq!(status, StatusCode::BAD_REQUEST);
        assert_eq!(body, json!({ "error": "bad request body" }));
    }

    #[test]
    fn presented_token_accepts_only_a_valid_header_value() {
        let mut headers = HeaderMap::new();
        assert_eq!(presented_token(&headers), None);

        headers.insert(TOKEN_HEADER, HeaderValue::from_static("secret"));
        assert_eq!(presented_token(&headers).as_deref(), Some("secret"));

        headers.insert(
            TOKEN_HEADER,
            HeaderValue::from_bytes(&[0xff]).expect("opaque header value"),
        );
        assert_eq!(presented_token(&headers), None);
    }
}

#[cfg(test)]
mod request_tests {
    use super::*;

    #[test]
    fn current_requests_reject_unknown_fields_without_compatibility_rewrites() {
        assert!(parse_session(json!({"name":"agent", "netwrok":"none"})).is_err());
        assert!(parse_project(json!({"name":"repo", "unexpected":true})).is_err());
        let project = parse_project(json!({"name":"repo", "dir":"/work/repo",
            "mounts":[{"from":"/work/shared", "to":"/mnt/shared", "mode":"ro"}]}))
        .unwrap();
        assert_eq!(project.mounts[0].from, "/work/shared");
        assert!(parse_template(json!({"name":"review", "defaults":{"unexpected":true}})).is_err());
    }
}
