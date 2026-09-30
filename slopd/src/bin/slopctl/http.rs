use crate::shared::{http_wire, wire};
use prost::Message;
use serde::Deserialize;
use serde_json::{Value, json};
use std::io::Read;
use std::path::PathBuf;

const TOKEN_HEADER: &str = crate::shared::protocol::TOKEN_HEADER;
const SESSION_HEADER: &str = crate::shared::protocol::SESSION_HEADER;

#[derive(Deserialize)]
pub(crate) struct Endpoint {
    pub(crate) url: String,
    pub(crate) token: String,
}

pub(crate) fn load_endpoint() -> Result<Endpoint, String> {
    if let Ok(url) = std::env::var("SLOPD_URL") {
        // Permit an explicitly empty SLOPD_TOKEN for a daemon without authentication.
        // Reject an absent variable here instead of sending an empty token that could cause an HTTP 401 response.
        let token = std::env::var("SLOPD_TOKEN").map_err(|_error| {
            "SLOPD_URL is set, but SLOPD_TOKEN is missing. Set SLOPD_TOKEN= if the daemon has no token."
                .to_string()
        })?;
        return Ok(Endpoint { url, token });
    }
    let path = std::env::var("SLOPD_ENDPOINT")
        .map(PathBuf::from)
        .unwrap_or_else(|_| {
            dirs::config_dir()
                .unwrap_or_else(|| PathBuf::from("."))
                .join("slopworld/endpoint.toml")
        });
    let text =
        std::fs::read_to_string(&path).map_err(|e| format!("reading {}: {e}", path.display()))?;
    toml::from_str(&text).map_err(|e| format!("parsing {}: {e}", path.display()))
}

pub(crate) fn request(
    endpoint: &Endpoint,
    session: &str,
    method: &str,
    path: &str,
    body: Option<Value>,
) -> Result<Value, String> {
    let url = format!("{}{}", endpoint.url.trim_end_matches('/'), path);
    // Read the response body before handling an error status.
    // The body explains why the daemon rejected the request. The HTTP status alone cannot supply this detail.
    let mut res = match (method, body) {
        ("GET" | "DELETE", None) => {
            let request = if method == "GET" {
                ureq::get(&url)
            } else {
                ureq::delete(&url)
            };
            request
                .config()
                .http_status_as_error(false)
                .build()
                .header(TOKEN_HEADER, &endpoint.token)
                .header(SESSION_HEADER, session)
                .call()
        }
        ("POST" | "PUT", Some(value)) => {
            let request = if method == "POST" {
                ureq::post(&url)
            } else {
                ureq::put(&url)
            };
            request
                .config()
                .http_status_as_error(false)
                .build()
                .header(TOKEN_HEADER, &endpoint.token)
                .header(SESSION_HEADER, session)
                .header("content-type", http_wire::CONTENT_TYPE)
                .send(http_wire::encode_request(method, path, value).map_err(|e| e.to_string())?)
        }
        _ => return Err(format!("unsupported request: {method}")),
    }
    .map_err(|e| format!("request: {e}"))?;
    let status = res.status();
    let content_type = res
        .headers()
        .get("content-type")
        .and_then(|v| v.to_str().ok())
        .unwrap_or("")
        .to_owned();
    let mut bytes = Vec::new();
    res.body_mut()
        .as_reader()
        .take(32 * 1024 * 1024 + 1)
        .read_to_end(&mut bytes)
        .map_err(|e| format!("response: {e}"))?;
    if bytes.len() > 32 * 1024 * 1024 {
        return Err("response exceeds limit".into());
    }
    if content_type != http_wire::CONTENT_TYPE {
        return Err(format!("response {status}: expected Protobuf protocol 2"));
    }
    if !status.is_success() {
        return Err(wire::Error::decode(bytes.as_slice())
            .map(|e| e.error)
            .unwrap_or_else(|_| format!("refused: {status}")));
    }
    let value = http_wire::decode_response(method, path, &bytes)
        .map_err(|e| format!("response {status}: {e}"))?;
    Ok(value)
}

/// Report the caller's identity, daemon connection status, and pending task counts.
/// Include connection failures in the status value instead of returning an error.
pub(crate) fn status_value(endpoint: &Endpoint, session: &str) -> Value {
    let mut out = serde_json::Map::new();
    out.insert("session".to_string(), json!(session));
    out.insert("endpoint".to_string(), json!(endpoint.url));
    match request(
        endpoint,
        session,
        "GET",
        crate::shared::protocol::routes::HEALTH,
        None,
    ) {
        Ok(v) => {
            out.insert("daemon".to_string(), json!("ok"));
            out.insert(
                "version".to_string(),
                v.get("version").cloned().unwrap_or(Value::Null),
            );
        }
        Err(e) => {
            out.insert("daemon".to_string(), json!("unreachable"));
            out.insert("error".to_string(), json!(e));
            return Value::Object(out);
        }
    }
    match request(
        endpoint,
        session,
        "GET",
        crate::shared::protocol::routes::TASKS,
        None,
    ) {
        Ok(v) => {
            let tasks = v
                .get("tasks")
                .and_then(Value::as_array)
                .cloned()
                .unwrap_or_default();
            let open = |t: &Value| {
                !matches!(
                    t.get("status").and_then(Value::as_str).unwrap_or(""),
                    crate::shared::protocol::enums::task_status::DONE
                        | crate::shared::protocol::enums::task_status::FAILED
                        | crate::shared::protocol::enums::task_status::CANCELED
                )
            };
            out.insert(
                "waiting".to_string(),
                json!(
                    tasks
                        .iter()
                        .filter(|t| t.get("to").and_then(Value::as_str) == Some(session) && open(t))
                        .count()
                ),
            );
            out.insert(
                "sent".to_string(),
                json!(
                    tasks
                        .iter()
                        .filter(
                            |t| t.get("from").and_then(Value::as_str) == Some(session) && open(t)
                        )
                        .count()
                ),
            );
        }
        Err(e) => {
            out.insert("error".to_string(), json!(e));
        }
    }
    Value::Object(out)
}
