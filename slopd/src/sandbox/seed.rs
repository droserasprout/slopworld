//! Initialize private preset copies through staged, bounded, non-following traversal.
//! Network preparation owns resolver files; state owns private-copy paths and lifetime.

use std::path::{Path, PathBuf};

use anyhow::{Context, Result};

use crate::config::expand;
use crate::presets::SandboxPreset;

fn metadata(path: &Path) -> Result<Option<std::fs::Metadata>> {
    match std::fs::symlink_metadata(path) {
        Ok(meta) => Ok(Some(meta)),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(None),
        Err(error) => Err(error).with_context(|| format!("reading {}", path.display())),
    }
}

/// Missing host sources are optional. Existing copies preserve all agent changes.
/// Source symlinks are omitted, including directory links and dangling links.
/// A private root must itself be a real file or directory.
pub(super) fn seed_into(pr: &SandboxPreset, host: &str, copy: &Path) -> Result<()> {
    if metadata(copy)?.is_some() {
        return Ok(());
    }
    let host = Path::new(host);
    let Some(meta) = metadata(host)? else {
        return Ok(());
    };
    anyhow::ensure!(
        !meta.file_type().is_symlink(),
        "private source {} must not be a symlink",
        host.display()
    );
    anyhow::ensure!(
        meta.is_file() || meta.is_dir(),
        "private source {} must be a file or directory",
        host.display()
    );
    let parent = copy.parent().context("private copy has no parent")?;
    std::fs::create_dir_all(parent)?;
    let stage = parent.join(format!(".seed-{}", uuid::Uuid::new_v4()));
    let result = (|| {
        copy_selected(pr, host, &stage, meta.is_file())?;
        publish(&stage, copy)
    })();
    // Clean failed staging and a staged copy superseded by another initializer.
    if let Ok(Some(meta)) = metadata(&stage) {
        if meta.is_dir() {
            drop(std::fs::remove_dir_all(&stage));
        } else {
            drop(std::fs::remove_file(&stage));
        }
    }
    result.with_context(|| format!("seeding {} from {}", copy.display(), host.display()))
}

fn copy_selected(pr: &SandboxPreset, host: &Path, stage: &Path, file: bool) -> Result<()> {
    if file {
        std::fs::copy(host, stage)?;
        return Ok(());
    }
    std::fs::create_dir(stage)?;
    let skip: Vec<PathBuf> = pr
        .skip
        .iter()
        .chain(&pr.shared)
        .map(|raw| expand(raw))
        .filter(|path| !path.is_empty())
        .map(PathBuf::from)
        .collect();
    for entry in std::fs::read_dir(host)? {
        let entry = entry?;
        if entry.file_type()?.is_file() {
            seed(&entry.path(), &stage.join(entry.file_name()), &skip, 0)?;
        }
    }
    for raw in &pr.seed {
        let from = PathBuf::from(expand(raw));
        let Ok(rel) = from.strip_prefix(host) else {
            continue;
        };
        // Preset validation guards containment, but do not let lexical traversal
        // escape staging when this helper is used independently.
        anyhow::ensure!(
            !rel.components()
                .any(|c| matches!(c, std::path::Component::ParentDir)),
            "seed path contains parent traversal"
        );
        // A seed's intermediate parents must not be symlinks either.
        let mut ancestor = host.to_path_buf();
        let mut linked = false;
        for component in rel.components() {
            ancestor.push(component);
            if metadata(&ancestor)?.is_some_and(|meta| meta.file_type().is_symlink()) {
                linked = true;
                break;
            }
        }
        if !linked {
            seed(&from, &stage.join(rel), &skip, 0)?;
        }
    }
    Ok(())
}

fn seed(from: &Path, to: &Path, skip: &[PathBuf], depth: usize) -> Result<()> {
    if skip.iter().any(|path| from.starts_with(path)) {
        return Ok(());
    }
    let Some(meta) = metadata(from)? else {
        return Ok(());
    };
    if meta.file_type().is_symlink() {
        return Ok(());
    }
    anyhow::ensure!(
        depth < 128,
        "seed directory nesting exceeds 128 at {}",
        from.display()
    );
    if meta.is_file() {
        if let Some(parent) = to.parent() {
            std::fs::create_dir_all(parent)?;
        }
        std::fs::copy(from, to)?;
    } else if meta.is_dir() {
        std::fs::create_dir_all(to)?;
        for entry in std::fs::read_dir(from)? {
            let entry = entry?;
            seed(&entry.path(), &to.join(entry.file_name()), skip, depth + 1)?;
        }
    } else {
        anyhow::bail!("seed {} is not a regular file or directory", from.display());
    }
    Ok(())
}

#[cfg(target_os = "linux")]
fn publish(stage: &Path, copy: &Path) -> Result<()> {
    use nix::fcntl::{RenameFlags, renameat2};
    match renameat2(None, stage, None, copy, RenameFlags::RENAME_NOREPLACE) {
        Ok(()) | Err(nix::errno::Errno::EEXIST) => Ok(()),
        Err(error) => Err(error.into()),
    }
}

#[cfg(not(target_os = "linux"))]
fn publish(stage: &Path, copy: &Path) -> Result<()> {
    anyhow::ensure!(
        metadata(copy)?.is_none(),
        "private copy appeared during initialization"
    );
    std::fs::rename(stage, copy)?;
    Ok(())
}

#[cfg(test)]
#[path = "seed_tests.rs"]
mod tests;
