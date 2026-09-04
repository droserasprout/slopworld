use serde::Deserialize;
use serde_json::{json, Value};
use std::io::Read;
use std::path::PathBuf;

#[derive(Deserialize)]
pub(crate) struct Endpoint {
    pub(crate) url: String,
    pub(crate) token: String,
}

pub(crate) fn load_endpoint() -> Result<Endpoint, String> {
    if let Ok(url) = std::env::var("SLOPD_URL") {
        // An empty root token is the daemon's "no auth" contract, so `SLOPD_TOKEN=` is a real
        // answer and kept. Unset is not one - it is the variable that was forgotten - and sending
        // an empty token on its behalf only turns the mistake into a 401 raised somewhere else.
        let token = std::env::var("SLOPD_TOKEN").map_err(|_| {
            "SLOPD_URL is set but SLOPD_TOKEN is not; \
             set SLOPD_TOKEN= for a daemon with no token"
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
    // A refusal carries the daemon's reason in its body, and that sentence is the whole value of
    // the reply - "a task still in flight cannot be removed" tells the user what to do next, where
    // a bare 400 does not. So a status is not an error here; it is read below, body first.
    let mut res = match (method, body) {
        ("GET", None) => ureq::get(&url)
            .config()
            .http_status_as_error(false)
            .build()
            .header("x-slop-token", &endpoint.token)
            .header("x-slop-session", session)
            .call(),
        ("DELETE", None) => ureq::delete(&url)
            .config()
            .http_status_as_error(false)
            .build()
            .header("x-slop-token", &endpoint.token)
            .header("x-slop-session", session)
            .call(),
        ("POST", Some(value)) => ureq::post(&url)
            .config()
            .http_status_as_error(false)
            .build()
            .header("x-slop-token", &endpoint.token)
            .header("x-slop-session", session)
            .send_json(value),
        _ => return Err(format!("unsupported request: {method}")),
    }
    .map_err(|e| format!("request: {e}"))?;
    let status = res.status();
    let mut text = String::new();
    res.body_mut()
        .as_reader()
        .read_to_string(&mut text)
        .map_err(|e| format!("response: {e}"))?;
    let value: Value = match serde_json::from_str(&text) {
        Ok(value) => value,
        Err(e) if status.is_success() => return Err(format!("response {status}: {e}")),
        // A refusal from the layer above the handlers - `auth`, which answers 401 bare - has no
        // JSON body to quote, so the status is the whole of what happened.
        Err(_) if text.trim().is_empty() => return Err(format!("refused: {status}")),
        Err(_) => return Err(format!("refused: {status}: {}", text.trim())),
    };
    if !status.is_success() {
        return Err(value
            .get("error")
            .and_then(Value::as_str)
            .unwrap_or(&text)
            .to_string());
    }
    Ok(value)
}

/// Identity, reachability and what is waiting - the three things to check before believing any
/// other answer this CLI gives. Unreachable is reported, not raised: that *is* the status.
pub(crate) fn status_value(endpoint: &Endpoint, session: &str) -> Value {
    let mut out = json!({ "session": session, "endpoint": endpoint.url });
    match request(endpoint, session, "GET", "/api/health", None) {
        Ok(v) => {
            out["daemon"] = json!("ok");
            out["version"] = v["version"].clone();
        }
        Err(e) => {
            out["daemon"] = json!("unreachable");
            out["error"] = json!(e);
            return out;
        }
    }
    match request(endpoint, session, "GET", "/api/tasks", None) {
        Ok(v) => {
            let tasks = v
                .get("tasks")
                .and_then(Value::as_array)
                .cloned()
                .unwrap_or_default();
            let open = |t: &Value| !matches!(t["status"].as_str().unwrap_or(""), "done" | "failed");
            out["waiting"] = json!(tasks
                .iter()
                .filter(|t| t["to"].as_str() == Some(session) && open(t))
                .count());
            out["sent"] = json!(tasks
                .iter()
                .filter(|t| t["from"].as_str() == Some(session) && open(t))
                .count());
        }
        Err(e) => out["error"] = json!(e),
    }
    out
}
