//! Shared preset cache directories. Preview resolves paths; only launch creates them.
use std::path::Path;

use anyhow::{Context, Result, ensure};

use crate::config::expand;
use crate::presets::SandboxPreset;

pub(super) fn paths(presets: &[&SandboxPreset]) -> Result<Vec<String>> {
    let mut paths = Vec::new();
    for raw in presets.iter().flat_map(|preset| &preset.cache) {
        let path = expand(raw);
        if path.is_empty() || paths.contains(&path) {
            continue;
        }
        ensure!(
            Path::new(&path).is_absolute(),
            "preset cache must be absolute: {path}"
        );
        if let Some(reason) = super::refused(&path) {
            anyhow::bail!("preset cache {path} reaches {reason}");
        }
        paths.push(path);
    }
    Ok(paths)
}

pub(crate) fn prepare(paths: &[String]) -> Result<()> {
    for path in paths {
        std::fs::create_dir_all(path)
            .with_context(|| format!("creating shared preset cache {path}"))?;
    }
    Ok(())
}

#[cfg(test)]
#[path = "preset_cache_tests.rs"]
mod tests;
