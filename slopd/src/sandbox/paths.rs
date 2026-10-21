//! Resolve host aliases for fail-closed path guards and validate preset dependencies.

use std::path::{Path, PathBuf};

use crate::paths::normalize as safety_path;
use anyhow::Result;

use crate::config::{Config, expand};
use crate::presets::{SandboxPreset, Table};

use super::state_root;

/// Ordinary binds may select a platform path list with `${VAR:paths}`.
/// Keep entries literal, omit empty entries, and validate each path independently.
/// Other fields retain single-path expansion because they own individual state roots.
pub(super) fn expand_bind_paths(raw: &str) -> Vec<String> {
    if let Some(name) = raw
        .strip_prefix("${")
        .and_then(|s| s.strip_suffix(":paths}"))
    {
        return std::env::var_os(name)
            .map(|value| {
                std::env::split_paths(&value)
                    .filter(|path| !path.as_os_str().is_empty())
                    .map(|path| path.to_string_lossy().into_owned())
                    .collect()
            })
            .unwrap_or_default();
    }
    vec![expand(raw)]
}

/// Reject paths that expose daemon credentials, configuration, preset definitions, or private session state.
/// Also reject ancestors and descendants of these protected paths.
pub fn refused(path: &str) -> Option<String> {
    let path = match safety_path(Path::new(path)) {
        Ok(path) => path,
        Err(error) => return Some(format!("an unresolved safety path: {error:#}")),
    };
    if path == Path::new("/") {
        return Some("the whole filesystem".into());
    }
    if let Some(home) = dirs::home_dir() {
        match safety_path(&home) {
            Ok(home) if home == path => return Some("the whole home directory".into()),
            Err(error) => return Some(format!("an unresolved home path: {error:#}")),
            _ => {}
        }
    }

    let keep = [
        ("daemon configuration stores", crate::paths::config_root()),
        ("agent records", crate::paths::data_root().join("agents")),
        (
            "host shell records",
            crate::paths::data_root().join("host_shells"),
        ),
        (
            "worktree records",
            crate::paths::data_root().join("worktrees"),
        ),
        ("task records", crate::paths::data_root().join("tasks")),
        (
            "scoped grants",
            crate::paths::data_root().join("grants.toml"),
        ),
        (
            "workspace recovery journal",
            crate::paths::data_root().join("workspace.save-journal"),
        ),
        ("daemon configuration and token", Config::path_in_use()),
        ("daemon endpoint file and token", crate::endpoint::path()),
        ("the preset files", Table::dir()),
        ("session private state", state_root()),
    ];
    for (what, kept) in keep {
        let kept = match safety_path(&kept) {
            Ok(kept) => kept,
            Err(error) => return Some(format!("an unresolved protected path: {error:#}")),
        };
        if kept.starts_with(&path) || path.starts_with(&kept) {
            return Some(what.into());
        }
    }
    None
}

/// Check aliases as well as literal spellings when a source could expose private originals.
pub(super) fn overlaps(left: &str, right: &str) -> bool {
    let (Ok(left), Ok(right)) = (safety_path(Path::new(left)), safety_path(Path::new(right)))
    else {
        return true; // An unresolved alias cannot establish disjointness.
    };
    left.starts_with(&right) || right.starts_with(&left)
}

/// Nested roots share private storage ancestors. Preparing either first can make
/// the other look initialized, so reject this ambiguity before creating copies.
pub(super) fn validate_private_roots<'a>(roots: impl Iterator<Item = &'a String>) -> Result<()> {
    let roots = roots
        .map(|raw| expand(raw))
        .filter(|path| !path.is_empty())
        .map(|path| {
            let literal = PathBuf::from(path);
            let resolved = safety_path(&literal)?;
            Ok((literal, resolved))
        })
        .collect::<Result<Vec<_>>>()?;
    for (index, (root, resolved)) in roots.iter().enumerate() {
        for (other, other_resolved) in roots.iter().take(index) {
            // Literal paths determine private storage; resolved paths determine
            // host overlap. An intermediate symlink can make these disagree.
            if [(root, other), (resolved, other_resolved)]
                .iter()
                .any(|(a, b)| a != b && (a.starts_with(b) || b.starts_with(a)))
            {
                anyhow::bail!(
                    "private roots {} and {} overlap; use one private root with seed entries",
                    root.display(),
                    other.display()
                );
            }
        }
    }
    Ok(())
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
    validate_private_roots(p.private.iter())?;
    if p.name.trim().is_empty() {
        anyhow::bail!("Enter a sandbox preset name.");
    }
    if p.requires.iter().any(|name| name.trim().is_empty()) {
        anyhow::bail!("Sandbox preset {:?} has a dependency with no name.", p.name);
    }

    for (kind, paths) in [
        ("read-only", p.ro.as_slice()),
        ("read-write", p.rw.as_slice()),
        ("cache", p.cache.as_slice()),
        ("device", p.dev.as_slice()),
        ("private", p.private.as_slice()),
        ("seed", p.seed.as_slice()),
        ("skip", p.skip.as_slice()),
        ("shared", p.shared.as_slice()),
    ] {
        for raw in paths {
            let expanded_paths = if matches!(kind, "read-only" | "read-write" | "device") {
                expand_bind_paths(raw)
            } else {
                vec![expand(raw)]
            };
            for expanded in expanded_paths {
                if expanded.is_empty() {
                    continue;
                }
                // Launch runs from the project directory, not the daemon's cwd.
                // A relative spelling could therefore mount a different host source.
                if !Path::new(&expanded).is_absolute() {
                    anyhow::bail!(
                        "Sandbox preset {:?} {kind} path {raw:?} must be absolute after expansion.",
                        p.name
                    );
                }
                if let Some(what) = refused(&expanded) {
                    anyhow::bail!(
                        "Sandbox preset {:?} {kind} path {raw:?} exposes {what}.",
                        p.name
                    );
                }
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
        .collect::<Result<_>>()?;

    for (kind, paths) in [("seed", p.seed.as_slice()), ("skip", p.skip.as_slice())] {
        for raw in paths {
            let expanded = expand(raw);
            if expanded.is_empty() {
                continue;
            }
            let path = safety_path(Path::new(&expanded))?;
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
        let path = safety_path(Path::new(&expanded))?;
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
