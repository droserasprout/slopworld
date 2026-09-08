//! Preset catalog and persistence HTTP boundaries.

use axum::extract::{Path, State};
use axum::http::StatusCode;
use axum::Json;
use serde_json::json;

use super::super::types::*;
use super::{err, ApiResult, Mgr};

/// So the GUI draws a checkbox per preset and a row per command rather than a list
/// somebody keeps in step by hand. Both tables are files, so this is also how the mod
/// learns about one that was added while it was running. `source` is explicit because a
/// merged table alone cannot tell a built-in from a user override.
pub(crate) async fn presets(State(_m): State<Mgr>) -> ApiResult {
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

    Ok(Json(json!({
        "presets": sandbox,
        "commands": commands,
        "dir": crate::presets::Table::dir(),
    })))
}

pub(crate) fn source(has_builtin: bool, has_user: bool) -> &'static str {
    match (has_builtin, has_user) {
        (true, true) => "override",
        (true, false) => "system",
        (false, true) => "user",
        (false, false) => "unknown",
    }
}

pub(crate) fn sandbox_json(
    p: &crate::presets::SandboxPreset,
    builtins: &crate::presets::Table,
    users: &crate::presets::Table,
) -> serde_json::Value {
    json!({
        "name": p.name,
        "source": source(builtins.sandbox(&p.name).is_some(), users.sandbox(&p.name).is_some()),
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
        "source": source(builtins.command(&c.name).is_some(), users.command(&c.name).is_some()),
        "description": c.description,
        "cmd": c.cmd,
        "sandbox": c.sandbox,
    })
}

pub(crate) fn valid_kind(kind: &str) -> Result<(), (StatusCode, Json<serde_json::Value>)> {
    if kind == "sandbox" || kind == "command" {
        Ok(())
    } else {
        Err(err(
            StatusCode::BAD_REQUEST,
            format!("unknown preset kind: {kind}"),
        ))
    }
}

pub(crate) async fn copy_preset(
    State(m): State<Mgr>,
    Path((kind, old_name)): Path<(String, String)>,
    body: Option<Json<CopyPresetReq>>,
) -> ApiResult {
    valid_kind(&kind)?;
    let target = body.map(|Json(body)| body.name).unwrap_or_default();
    let name = if target.trim().is_empty() {
        old_name.clone()
    } else {
        target
    };
    let builtins = crate::presets::Table::builtins();
    let users = crate::presets::Table::users();
    let already_user = if kind == "sandbox" {
        users.sandbox(&old_name).is_some()
    } else {
        users.command(&old_name).is_some()
    };
    if already_user {
        return Err(err(
            StatusCode::BAD_REQUEST,
            "that preset already has a user definition",
        ));
    }
    let target_exists = if kind == "sandbox" {
        users.sandbox(&name).is_some() || (name != old_name && builtins.sandbox(&name).is_some())
    } else {
        users.command(&name).is_some() || (name != old_name && builtins.command(&name).is_some())
    };
    if target_exists {
        return Err(err(
            StatusCode::BAD_REQUEST,
            "the target name already exists",
        ));
    }

    if kind == "sandbox" {
        let Some(mut preset) = builtins.sandbox(&old_name).cloned() else {
            return Err(err(
                StatusCode::NOT_FOUND,
                format!("unknown sandbox preset: {old_name}"),
            ));
        };
        preset.name = name;
        crate::presets::save_sandbox(preset).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    } else {
        let Some(mut preset) = builtins.command(&old_name).cloned() else {
            return Err(err(
                StatusCode::NOT_FOUND,
                format!("unknown command preset: {old_name}"),
            ));
        };
        preset.name = name;
        crate::presets::save_command(preset).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    }
    m.reload_presets_if_changed().await;
    Ok(Json(json!({ "ok": true })))
}

pub(crate) async fn update_preset(
    State(m): State<Mgr>,
    Path((kind, name)): Path<(String, String)>,
    body: axum::body::Bytes,
) -> ApiResult {
    valid_kind(&kind)?;
    if name.trim().is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "preset name is empty"));
    }
    if kind == "sandbox" {
        let mut preset: crate::presets::SandboxPreset =
            serde_json::from_slice(&body).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
        preset.name = name;
        let current = crate::presets::table();
        let mut candidate = (*current).clone();
        candidate
            .sandbox
            .retain(|existing| existing.name != preset.name);
        candidate.sandbox.push(preset.clone());
        crate::sandbox::validate_preset(&preset, &candidate)
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
        crate::presets::save_sandbox(preset).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    } else {
        let mut preset: crate::presets::CommandPreset =
            serde_json::from_slice(&body).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
        preset.name = name;
        if preset.cmd.trim().is_empty() {
            return Err(err(StatusCode::BAD_REQUEST, "command line is empty"));
        }
        let table = crate::presets::table();
        for dep in &preset.sandbox {
            crate::sandbox::validate_preset_name(dep, &table).map_err(|e| {
                err(
                    StatusCode::BAD_REQUEST,
                    format!("invalid sandbox dependency {dep:?}: {e}"),
                )
            })?;
        }
        crate::presets::save_command(preset).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    }
    m.reload_presets_if_changed().await;
    Ok(Json(json!({ "ok": true })))
}

pub(crate) async fn delete_preset(
    State(m): State<Mgr>,
    Path((kind, name)): Path<(String, String)>,
) -> ApiResult {
    valid_kind(&kind)?;
    let builtins = crate::presets::Table::builtins();
    let users = crate::presets::Table::users();
    let has_user = if kind == "sandbox" {
        users.sandbox(&name).is_some()
    } else {
        users.command(&name).is_some()
    };
    if !has_user {
        return Err(err(
            StatusCode::NOT_FOUND,
            "no user definition exists for that preset",
        ));
    }
    if kind == "sandbox" {
        if builtins.sandbox(&name).is_none() {
            // A user-only sandbox cannot disappear while a command still requires it. Check
            // before writing, or a rejected delete would have already changed the directory.
            let remaining = crate::presets::Table::users();
            for command in remaining.commands.iter().chain(builtins.commands.iter()) {
                if command.sandbox.iter().any(|dependency| dependency == &name) {
                    return Err(err(
                        StatusCode::BAD_REQUEST,
                        format!("sandbox {name:?} is required by command {:?}", command.name),
                    ));
                }
            }
            for preset in remaining.sandbox.iter().chain(builtins.sandbox.iter()) {
                if preset.requires.iter().any(|dependency| dependency == &name) {
                    return Err(err(
                        StatusCode::BAD_REQUEST,
                        format!("sandbox {name:?} is required by sandbox {:?}", preset.name),
                    ));
                }
            }
        }
        crate::presets::remove_sandbox(&name).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    } else {
        crate::presets::remove_command(&name).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    }
    m.reload_presets_if_changed().await;
    Ok(Json(json!({ "ok": true })))
}
