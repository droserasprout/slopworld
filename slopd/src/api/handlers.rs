use crate::api::protobuf::reply;
use axum::http::StatusCode;

use serde_json::json;

use crate::grant::{Cap, Level};

use super::{err, ApiResult, Mgr};

#[path = "handlers_settings.rs"]
mod handlers_settings;
pub(crate) use handlers_settings::*;

#[path = "handlers_clipboard.rs"]
mod handlers_clipboard;
#[path = "handlers_config.rs"]
mod handlers_config;
#[path = "handlers_files.rs"]
mod handlers_files;
#[path = "handlers_grants.rs"]
mod handlers_grants;
#[path = "handlers_jukebox.rs"]
mod handlers_jukebox;
#[path = "handlers_library.rs"]
mod handlers_library;
#[path = "handlers_presets.rs"]
mod handlers_presets;
#[path = "handlers_sessions.rs"]
mod handlers_sessions;
#[path = "handlers_system.rs"]
mod handlers_system;
#[path = "handlers_tasks.rs"]
mod handlers_tasks;
#[path = "handlers_templates.rs"]
mod handlers_templates;

pub(crate) use handlers_clipboard::*;
pub(crate) use handlers_config::*;
pub(crate) use handlers_files::*;
pub(crate) use handlers_grants::*;
pub(crate) use handlers_jukebox::*;
pub(crate) use handlers_library::*;
pub(crate) use handlers_presets::*;
pub(crate) use handlers_sessions::*;
pub(crate) use handlers_system::*;
pub(crate) use handlers_tasks::*;
pub(crate) use handlers_templates::*;
fn ok_json(r: anyhow::Result<()>) -> ApiResult {
    r.map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "ok": true }))
}

/// Return 403 for denied or unknown names without revealing session existence.
pub(super) async fn guard(
    m: &Mgr,
    cap: &Cap,
    name: &str,
    need: Level,
) -> Result<(), crate::api::protobuf::ApiError> {
    if m.cap_ok(cap, name, need).await {
        Ok(())
    } else {
        Err(err(StatusCode::FORBIDDEN, format!("not allowed: {name}")))
    }
}

/// Only root may create sessions.
pub(super) fn guard_create(cap: &Cap) -> Result<(), crate::api::protobuf::ApiError> {
    if cap.may_create() {
        Ok(())
    } else {
        Err(err(
            StatusCode::FORBIDDEN,
            "only the daemon's own token may create sessions",
        ))
    }
}

/// Replacing sandbox configuration requires root authority.
pub(super) fn guard_root(cap: &Cap) -> Result<(), crate::api::protobuf::ApiError> {
    if cap.may_create() {
        Ok(())
    } else {
        Err(err(
            StatusCode::FORBIDDEN,
            "only the daemon's own token may replace session configuration",
        ))
    }
}

/// Text for the raw editor, parsed for the settings GUI, so a mod can offer either
/// without parsing TOML.
#[cfg(test)]
#[path = "handlers_tests.rs"]
mod tests;
