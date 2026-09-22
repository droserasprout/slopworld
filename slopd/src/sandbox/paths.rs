use std::ffi::OsString;
use std::path::{Component, Path, PathBuf};

use anyhow::Result;

use crate::config::{expand, Config};
use crate::presets::{SandboxPreset, Table};

use super::state_root;

/// Rejects paths reaching the daemon token/config, preset definitions, or another session's
/// private state, including paths above or below those protected roots.
pub fn refused(path: &str) -> Option<String> {
    let path = safety_path(Path::new(path));
    if path == Path::new("/") {
        return Some("the whole filesystem".into());
    }
    if dirs::home_dir().is_some_and(|home| safety_path(&home) == path) {
        return Some("the whole home directory".into());
    }

    let keep = [
        (
            "the daemon's config, and the token in it",
            Config::path_in_use(),
        ),
        (
            "the daemon's endpoint descriptor, and its token",
            crate::endpoint::path(),
        ),
        ("the preset files", Table::dir()),
        ("what the sessions keep to themselves", state_root()),
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

/// Resolve a path as far as the filesystem lets us, retaining any missing suffix. This keeps
/// safety checks aware of symlinks in existing parents while still allowing portable presets to
/// name software that is not installed on this machine yet.
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

/// Normalize `.` and `..` without following symlinks. `safety_path` follows symlinks first where
/// possible, then uses this only for the missing suffix or paths whose parents do not exist.
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

/// Validate one effective sandbox preset before either exposing it to bwrap or saving it from
/// the API. Keeping this beside argv construction prevents the API, hand-edited files and old
/// config entries from growing subtly different safety rules.
pub fn validate_preset(p: &SandboxPreset, table: &Table) -> Result<()> {
    validate_preset_fields(p, table)?;
    let mut visiting = vec![p.name.clone()];
    for required in &p.requires {
        validate_preset_name_inner(required, table, &mut visiting)?;
    }
    Ok(())
}

/// Validate a named preset and its complete dependency closure. A dependent preset is invalid
/// when any required preset is invalid, so runtime selection cannot silently apply only half of
/// a capability bundle.
pub fn validate_preset_name(name: &str, table: &Table) -> Result<()> {
    validate_preset_name_inner(name, table, &mut Vec::new())
}

fn validate_preset_name_inner(name: &str, table: &Table, visiting: &mut Vec<String>) -> Result<()> {
    if visiting.iter().any(|seen| seen == name) {
        anyhow::bail!("sandbox preset dependency cycle at {name:?}");
    }
    let p = table
        .sandbox(name)
        .ok_or_else(|| anyhow::anyhow!("unknown sandbox preset: {name}"))?;
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
            anyhow::bail!("sandbox preset {:?} requires itself", p.name);
        }
        if table.sandbox(required).is_none() {
            anyhow::bail!(
                "sandbox preset {:?} requires unknown preset {:?}",
                p.name,
                required
            );
        }
    }
    Ok(())
}

fn validate_preset_paths(p: &SandboxPreset) -> Result<()> {
    if p.name.trim().is_empty() {
        anyhow::bail!("sandbox preset name is empty");
    }
    if p.requires.iter().any(|name| name.trim().is_empty()) {
        anyhow::bail!("sandbox preset {:?} has an empty dependency", p.name);
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
                    "sandbox preset {:?} {kind} path {raw:?} reaches {what}",
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
                    "sandbox preset {:?} {kind} path {raw:?} is outside its private paths",
                    p.name
                );
            }
        }
    }

    for raw in &p.shared {
        let expanded = expand(raw);
        if expanded.is_empty() {
            anyhow::bail!(
                "sandbox preset {:?} shared path {raw:?} contains an unset variable",
                p.name
            );
        }
        if Path::new(&expanded).exists() && !Path::new(&expanded).is_file() {
            anyhow::bail!(
                "sandbox preset {:?} shared path {raw:?} is not a regular file",
                p.name
            );
        }
        let path = safety_path(Path::new(&expanded));
        if !private.iter().any(|root| path.starts_with(root)) {
            anyhow::bail!(
                "sandbox preset {:?} shared path {raw:?} must be inside a private path",
                p.name
            );
        }
    }
    Ok(())
}

#[cfg(test)]
#[path = "paths_tests.rs"]
mod tests;
