//! Preset catalog and persistence HTTP boundaries.
use crate::api::protobuf::{Proto, domain, reply};
use crate::shared::wire;

use axum::extract::{Path, State};
use axum::http::StatusCode;

use serde_json::json;

use super::{ApiResult, Mgr, err};

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
    copied.map_err(preset_error)?;
    m.reload_presets_if_changed().await;
    Ok(Proto(wire::Ack { ok: true }))
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
            ));
        }
    };
    match &mut definition {
        crate::presets::PresetDefinition::Sandbox(preset) => preset.name = name,
        crate::presets::PresetDefinition::Command(preset) => preset.name = name,
    }
    tokio::task::spawn_blocking(move || crate::presets::validate_and_save(definition))
        .await
        .map_err(|error| err(StatusCode::INTERNAL_SERVER_ERROR, error))?
        .map_err(preset_error)?;
    m.reload_presets_if_changed().await;
    Ok(Proto(wire::Ack { ok: true }))
}

pub(crate) async fn delete_preset(
    State(m): State<Mgr>,
    Path((kind, name)): Path<(String, String)>,
) -> ApiResult<wire::Ack> {
    let kind = parse_kind(&kind)?;
    let deleted = tokio::task::spawn_blocking(move || crate::presets::delete_user(kind, &name))
        .await
        .map_err(|error| err(StatusCode::INTERNAL_SERVER_ERROR, error))?;
    deleted.map_err(preset_error)?;
    m.reload_presets_if_changed().await;
    Ok(Proto(wire::Ack { ok: true }))
}

fn preset_error(error: crate::presets::PresetError) -> crate::api::protobuf::ApiError {
    let status = match &error {
        crate::presets::PresetError::Missing(_) => StatusCode::NOT_FOUND,
        crate::presets::PresetError::Invalid(_) => StatusCode::BAD_REQUEST,
        crate::presets::PresetError::Storage(_) => StatusCode::INTERNAL_SERVER_ERROR,
    };
    err(status, error)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn update_distinguishes_validation_from_storage_failure() {
        let Some(root) = crate::test_support::isolated_with_env(|command, root| {
            command.env("SLOPD_PRESETS", root.join("blocked-presets"));
        }) else {
            return;
        };
        let blocked = root.join("blocked-presets");
        std::fs::write(&blocked, "not a directory").unwrap();
        let manager = crate::session::test_manager(crate::config::Config::default());
        let request = |command: &str| {
            Proto(wire::PresetRequest {
                definition: Some(wire::preset_request::Definition::Command(
                    wire::CommandPreset {
                        name: Some("example".into()),
                        kind: Some("agent".into()),
                        cmd: Some(command.into()),
                        ..Default::default()
                    },
                )),
            })
        };
        let path = || Path(("app_presets".into(), "example".into()));
        assert_eq!(
            update_preset(State(manager.clone()), path(), request(" "))
                .await
                .unwrap_err()
                .0,
            StatusCode::BAD_REQUEST
        );
        let (status, Proto(error)) = update_preset(State(manager), path(), request("echo ok"))
            .await
            .unwrap_err();
        assert_eq!(status, StatusCode::INTERNAL_SERVER_ERROR, "{}", error.error);
    }
}
