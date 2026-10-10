//! Mount policy protects host/container state; Docker owns argument transport.
use crate::settings::Settings;
use anyhow::{Context, Result, bail};
use std::{
    ffi::OsStr,
    path::{Path, PathBuf},
};

pub(crate) fn check_path(path: &Path) -> Result<()> {
    let text = path.to_str().context("mount paths must be UTF-8")?;
    if !path.is_absolute()
        || text.contains([',', '\n'])
        || text.split('/').any(|part| part == "." || part == "..")
    {
        bail!(
            "mount path must be absolute with no comma, newline, . or .. components: {}",
            path.display()
        );
    }
    Ok(())
}

// Resolve existing ancestors too: configuration/data need not have been created yet.
fn protected_path(path: &Path) -> Result<PathBuf> {
    check_path(path)?;
    match std::fs::symlink_metadata(path) {
        Ok(_) => path
            .canonicalize()
            .with_context(|| format!("cannot resolve protected path {}", path.display())),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
            let parent = path.parent().context("protected path has no parent")?;
            Ok(protected_path(parent)?
                .join(path.file_name().context("protected path has no name")?))
        }
        Err(error) => Err(error.into()),
    }
}

fn check_source(path: &Path, settings: &Settings) -> Result<PathBuf> {
    check_path(path)?;
    let resolved = path
        .canonicalize()
        .with_context(|| format!("mount source does not exist: {}", path.display()))?;
    let home = settings.home.canonicalize()?;
    if resolved == Path::new("/") || resolved == home {
        bail!("refusing to mount a filesystem or the whole home");
    }
    for protected in [&settings.config, &settings.data] {
        let protected = protected_path(protected)?;
        if resolved.starts_with(&protected) || protected.starts_with(&resolved) {
            bail!(
                "mount overlaps protected SlopWorld state: {}",
                path.display()
            );
        }
    }
    Ok(resolved)
}

pub(crate) fn workspace(path: &Path, settings: &Settings) -> Result<String> {
    check_source(path, settings)?;
    if !path.is_dir() {
        bail!("workspace is not a directory: {}", path.display());
    }
    Ok(format!(
        "type=bind,source={},target={}",
        path.display(),
        path.display()
    ))
}

pub(crate) fn credential(spec: &OsStr, readonly: bool, settings: &Settings) -> Result<String> {
    let (source, target) = spec
        .to_str()
        .context("credential must be UTF-8")?
        .split_once('=')
        .context("credential must be SOURCE=CONTAINER_TARGET")?;
    let source = Path::new(source);
    let target = Path::new(target);
    check_path(target)?;
    let resolved = check_source(source, settings)?;
    let metadata = std::fs::symlink_metadata(source)?;
    if metadata.file_type().is_symlink() || !(metadata.is_file() || metadata.is_dir()) {
        bail!("credential source must be a regular file or directory, not a symlink");
    }
    let docker_config = settings.home.canonicalize()?.join(".docker");
    if resolved.starts_with(docker_config) || resolved.to_string_lossy().contains("docker.sock") {
        bail!("the Docker configuration/socket is not a slopcar credential");
    }
    let container_home = Path::new("/home/slop");
    if target == container_home || !target.starts_with(container_home) {
        bail!("credential target must be below /home/slop");
    }
    for protected in [
        "/home/slop/.config/slopworld",
        "/home/slop/.local/share/slopworld",
    ] {
        let protected = Path::new(protected);
        if target.starts_with(protected) || protected.starts_with(target) {
            bail!("credential target overlaps protected SlopWorld state");
        }
    }
    Ok(format!(
        "type=bind,source={},target={}{}",
        source.display(),
        target.display(),
        if readonly { ",readonly" } else { "" }
    ))
}

pub(crate) fn state(path: &Path, target: &str) -> Result<String> {
    check_path(path)?;
    Ok(format!(
        "type=bind,source={},target={target}",
        path.display()
    ))
}

#[cfg(test)]
#[path = "mounts_tests.rs"]
mod tests;
