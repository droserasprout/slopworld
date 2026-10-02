use crate::api::protobuf::Proto;
use crate::shared::wire;
use axum::http::StatusCode;

use crate::grant::{Cap, Level};

use super::{ApiResult, Mgr, err};

mod settings;
pub(crate) use settings::*;

mod actions;
mod clipboard;
mod config;
#[cfg(test)]
pub(crate) use config::config_patch;
mod files;
mod grants;
mod highlighting;
mod jukebox;
mod library;
mod presets;
mod preview_scope;
mod sessions;
mod system;
mod tasks;
mod templates;

pub(crate) use actions::*;
pub(crate) use clipboard::*;
pub(crate) use config::*;
pub(crate) use files::*;
pub(crate) use grants::*;
pub(crate) use highlighting::*;
pub(crate) use jukebox::*;
pub(crate) use library::*;
pub(crate) use presets::*;
pub(crate) use sessions::*;
pub(crate) use system::*;
pub(crate) use tasks::*;
pub(crate) use templates::*;
fn ack_result(r: anyhow::Result<()>) -> ApiResult {
    r.map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(wire::Ack { ok: true }))
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

/// Only the root token can create sessions through this request.
pub(super) fn guard_create(cap: &Cap) -> Result<(), crate::api::protobuf::ApiError> {
    if cap.may_create() {
        Ok(())
    } else {
        Err(err(
            StatusCode::FORBIDDEN,
            "Only the root token can create sessions through this request.",
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
            "Only the root token can replace session configuration.",
        ))
    }
}

#[cfg(test)]
mod tests;

mod worktrees;
pub(crate) use worktrees::*;
