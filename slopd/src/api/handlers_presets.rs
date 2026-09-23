//! Preset catalog and persistence HTTP boundaries.
use crate::api::protobuf::{domain, reply, Proto};
use crate::shared::wire;

use axum::extract::{Path, State};
use axum::http::StatusCode;

use serde_json::json;

use super::{err, ApiResult, Mgr};

use crate::presets::PresetKind;

/// Supply preset and command catalogs for the GUI.
/// The mod can discover definitions added while it runs without maintaining a separate list.
/// Include `source` to distinguish built-in definitions from user overrides.
pub(crate) async fn presets(State(_m): State<Mgr>) -> ApiResult<wire::PresetsReply> {
    let effective = crate::presets::table();
    let builtins = crate::presets::Table::builtins();
    let users = crate::presets::Table::users();

    let sandbox: Vec<serde_json::Value> = effective
        .sandbox
        .iter()
        .map(|p| sandbox_json(p, &builtins, &users))
        .collect();
    let commands: Vec<serde_json::Value> = effective
        .commands
        .iter()
        .map(|c| command_json(c, &builtins, &users))
        .collect();

    reply(json!({
        "presets": sandbox,
        "commands": commands,
        "dir": crate::presets::Table::dir(),
    }))
}

pub(crate) fn sandbox_json(
    p: &crate::presets::SandboxPreset,
    builtins: &crate::presets::Table,
    users: &crate::presets::Table,
) -> serde_json::Value {
    json!({
        "name": p.name,
        "source": builtins.source(PresetKind::SandboxPresets, &p.name, users).as_str(),
        "description": p.description,
        "requires": p.requires,
        "ro": p.ro,
        "rw": p.rw,
        "dev": p.dev,
        "private": p.private,
        "seed": p.seed,
        "skip": p.skip,
        "shared": p.shared,
        "escapes": p.escapes,
        "env": p.env,
        "setenv": p.setenv,
        "tmux": p.tmux,
        "daemon_config": p.daemon_config,
    })
}

pub(crate) fn command_json(
    c: &crate::presets::CommandPreset,
    builtins: &crate::presets::Table,
    users: &crate::presets::Table,
) -> serde_json::Value {
    json!({
        "name": c.name,
        "kind": c.kind,
        "source": builtins.source(PresetKind::AppPresets, &c.name, users).as_str(),
        "description": c.description,
        "cmd": c.cmd,
        "sandbox": c.sandbox,
    })
}

pub(crate) fn parse_kind(kind: &str) -> Result<PresetKind, crate::api::protobuf::ApiError> {
    kind.parse()
        .map_err(|error: String| err(StatusCode::BAD_REQUEST, error))
}

pub(crate) async fn copy_preset(
    State(m): State<Mgr>,
    Path((kind, old_name)): Path<(String, String)>,
    body: Option<Proto<wire::CopyPresetReq>>,
) -> ApiResult<wire::Ack> {
    let kind = parse_kind(&kind)?;
    let target = body.and_then(|Proto(body)| body.name).unwrap_or_default();
    let name = if target.trim().is_empty() {
        old_name.clone()
    } else {
        target
    };
    let copied =
        tokio::task::spawn_blocking(move || crate::presets::copy_builtin(kind, &old_name, &name))
            .await
            .map_err(|error| err(StatusCode::INTERNAL_SERVER_ERROR, error))?;
    copied.map_err(|error| {
        let status = if matches!(&error, crate::presets::PresetError::Missing(_)) {
            StatusCode::NOT_FOUND
        } else {
            StatusCode::BAD_REQUEST
        };
        err(status, error)
    })?;
    m.reload_presets_if_changed().await;
    reply(json!({ "ok": true }))
}

pub(crate) async fn update_preset(
    State(m): State<Mgr>,
    Path((kind, name)): Path<(String, String)>,
    Proto(body): Proto<wire::PresetRequest>,
) -> ApiResult<wire::Ack> {
    let kind = parse_kind(&kind)?;
    if name.trim().is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "Enter a preset name."));
    }
    let mut definition = match (kind, body.definition) {
        (PresetKind::SandboxPresets, Some(wire::preset_request::Definition::Sandbox(mut p))) => {
            p.source = None;
            crate::presets::PresetDefinition::Sandbox(Box::new(domain(p)?))
        }
        (PresetKind::AppPresets, Some(wire::preset_request::Definition::Command(mut p))) => {
            p.source = None;
            crate::presets::PresetDefinition::Command(Box::new(domain(p)?))
        }
        _ => {
            return Err(err(
                StatusCode::BAD_REQUEST,
                "The kind field does not match the supplied definition.",
            ))
        }
    };
    match &mut definition {
        crate::presets::PresetDefinition::Sandbox(preset) => preset.name = name,
        crate::presets::PresetDefinition::Command(preset) => preset.name = name,
    }
    tokio::task::spawn_blocking(move || crate::presets::validate_and_save(definition))
        .await
        .map_err(|error| err(StatusCode::INTERNAL_SERVER_ERROR, error))?
        .map_err(|error| err(StatusCode::BAD_REQUEST, error))?;
    m.reload_presets_if_changed().await;
    reply(json!({ "ok": true }))
}

pub(crate) async fn delete_preset(
    State(m): State<Mgr>,
    Path((kind, name)): Path<(String, String)>,
) -> ApiResult<wire::Ack> {
    let kind = parse_kind(&kind)?;
    let deleted = tokio::task::spawn_blocking(move || crate::presets::delete_user(kind, &name))
        .await
        .map_err(|error| err(StatusCode::INTERNAL_SERVER_ERROR, error))?;
    deleted.map_err(|error| {
        let status = if matches!(&error, crate::presets::PresetError::Missing(_)) {
            StatusCode::NOT_FOUND
        } else {
            StatusCode::BAD_REQUEST
        };
        err(status, error)
    })?;
    m.reload_presets_if_changed().await;
    reply(json!({ "ok": true }))
}
