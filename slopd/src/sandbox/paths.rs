use std::ffi::OsString;
use std::path::{Component, Path, PathBuf};

use anyhow::Result;

use crate::config::{expand, Config};
use crate::presets::{SandboxPreset, Table};

use super::state_root;

/// Reject paths that expose daemon credentials, configuration, preset definitions, or private session state.
/// Also reject ancestors and descendants of these protected paths.
pub fn refused(path: &str) -> Option<String> {
    let path = safety_path(Path::new(path));
    if path == Path::new("/") {
        return Some("the whole filesystem".into());
    }
    if dirs::home_dir().is_some_and(|home| safety_path(&home) == path) {
        return Some("the whole home directory".into());
    }

    let keep = [
        ("daemon configuration and token", Config::path_in_use()),
        ("daemon endpoint file and token", crate::endpoint::path()),
        ("the preset files", Table::dir()),
        ("session private state", state_root()),
    ];
    for (what, kept) in keep {
        let kept = safety_path(&kept);
        if kept.starts_with(&path) || path.starts_with(&kept) {
            return Some(what.into());
        }
    }
    None
}

/// Check aliases as well as literal spellings when a source could expose private originals.
pub(super) fn overlaps(left: &str, right: &str) -> bool {
    let left = safety_path(Path::new(left));
    let right = safety_path(Path::new(right));
    left.starts_with(&right) || right.starts_with(&left)
}

/// Resolve existing path components and keep the missing suffix.
/// Safety checks then account for symlinks in existing parents.
/// Presets can still specify paths for software that this machine does not have.
fn safety_path(path: &Path) -> PathBuf {
    let mut missing: Vec<OsString> = Vec::new();
    let mut probe = path.to_path_buf();

    while !probe.exists() {
        let Some(name) = probe.file_name() else {
            return lexical_path(path);
        };
        missing.push(name.to_os_string());
        if !probe.pop() {
            return lexical_path(path);
        }
    }

    let mut out = std::fs::canonicalize(&probe).unwrap_or_else(|_| lexical_path(&probe));
    for name in missing.iter().rev() {
        out.push(name);
    }
    lexical_path(&out)
}

/// Normalize `.` and `..` without following symlinks.
/// `safety_path` first follows symlinks where possible.
/// It uses this function for missing suffixes or paths without existing parents.
fn lexical_path(path: &Path) -> PathBuf {
    let mut out = PathBuf::new();
    for component in path.components() {
        match component {
            Component::CurDir => {}
            Component::ParentDir => {
                out.pop();
            }
            other => out.push(other.as_os_str()),
        }
    }
    out
}

/// Validate a sandbox preset before bwrap uses it or the API saves it.
/// This check keeps safety rules consistent for API requests and manual file changes.
pub fn validate_preset(p: &SandboxPreset, table: &Table) -> Result<()> {
    validate_preset_fields(p, table)?;
    let mut visiting = vec![p.name.clone()];
    for required in &p.requires {
        validate_preset_name_inner(required, table, &mut visiting)?;
    }
    Ok(())
}

/// Validate a named preset and all its dependencies.
/// Reject the preset if any dependency is invalid.
/// This prevents runtime selection from applying an incomplete set of required presets.
pub fn validate_preset_name(name: &str, table: &Table) -> Result<()> {
    validate_preset_name_inner(name, table, &mut Vec::new())
}

fn validate_preset_name_inner(name: &str, table: &Table, visiting: &mut Vec<String>) -> Result<()> {
    if visiting.iter().any(|seen| seen == name) {
        anyhow::bail!("Sandbox preset {name:?} has a dependency cycle.");
    }
    let p = table
        .sandbox(name)
        .ok_or_else(|| anyhow::anyhow!("Sandbox preset {name:?} does not exist."))?;
    validate_preset_fields(p, table)?;
    visiting.push(name.to_string());
    for required in &p.requires {
        validate_preset_name_inner(required, table, visiting)?;
    }
    visiting.pop();
    Ok(())
}

fn validate_preset_fields(p: &SandboxPreset, table: &Table) -> Result<()> {
    validate_preset_paths(p)?;
    for required in &p.requires {
        if required == &p.name {
            anyhow::bail!("Sandbox preset {:?} cannot require itself.", p.name);
        }
        if table.sandbox(required).is_none() {
            anyhow::bail!(
                "Sandbox preset {:?} requires sandbox preset {:?}, but it does not exist.",
                p.name,
                required
            );
        }
    }
    Ok(())
}

fn validate_preset_paths(p: &SandboxPreset) -> Result<()> {
    if p.name.trim().is_empty() {
        anyhow::bail!("Enter a sandbox preset name.");
    }
    if p.requires.iter().any(|name| name.trim().is_empty()) {
        anyhow::bail!("Sandbox preset {:?} has a dependency with no name.", p.name);
    }

    for (kind, paths) in [
        ("read-only", p.ro.as_slice()),
        ("read-write", p.rw.as_slice()),
        ("device", p.dev.as_slice()),
        ("private", p.private.as_slice()),
        ("seed", p.seed.as_slice()),
        ("skip", p.skip.as_slice()),
        ("shared", p.shared.as_slice()),
    ] {
        for raw in paths {
            let expanded = expand(raw);
            if expanded.is_empty() {
                continue;
            }
            if let Some(what) = refused(&expanded) {
                anyhow::bail!(
                    "Sandbox preset {:?} {kind} path {raw:?} exposes {what}.",
                    p.name
                );
            }
        }
    }

    let private: Vec<PathBuf> = p
        .private
        .iter()
        .filter_map(|raw| {
            let expanded = expand(raw);
            (!expanded.is_empty()).then(|| safety_path(Path::new(&expanded)))
        })
        .collect();

    for (kind, paths) in [("seed", p.seed.as_slice()), ("skip", p.skip.as_slice())] {
        for raw in paths {
            let expanded = expand(raw);
            if expanded.is_empty() {
                continue;
            }
            let path = safety_path(Path::new(&expanded));
            if !private.iter().any(|root| path.starts_with(root)) {
                anyhow::bail!(
                    "Sandbox preset {:?} {kind} path {raw:?} must be inside a private path.",
                    p.name
                );
            }
        }
    }

    for raw in &p.shared {
        let expanded = expand(raw);
        if expanded.is_empty() {
            anyhow::bail!(
                "Sandbox preset {:?} shared path {raw:?} contains an unset variable.",
                p.name
            );
        }
        if Path::new(&expanded).exists() && !Path::new(&expanded).is_file() {
            anyhow::bail!(
                "Sandbox preset {:?} shared path {raw:?} is not a regular file.",
                p.name
            );
        }
        let path = safety_path(Path::new(&expanded));
        if !private.iter().any(|root| path.starts_with(root)) {
            anyhow::bail!(
                "Sandbox preset {:?} shared path {raw:?} must be inside a private path.",
                p.name
            );
        }
    }
    Ok(())
}

#[cfg(test)]
#[path = "paths_tests.rs"]
mod tests;
