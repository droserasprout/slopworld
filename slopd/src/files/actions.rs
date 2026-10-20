//! File-action path containment and shell command preparation. Execution belongs to the caller.
use crate::config::{ProjectCfg, expand};
use anyhow::{Result, bail};
use std::path::{Component, Path, PathBuf};

pub(crate) fn normalize_path(path: &Path) -> PathBuf {
    let mut out = PathBuf::new();
    for component in path.components() {
        match component {
            Component::RootDir => out.push(Path::new("/")),
            Component::CurDir => {}
            Component::ParentDir => {
                out.pop();
            }
            Component::Normal(name) => out.push(name),
            Component::Prefix(prefix) => out.push(prefix.as_os_str()),
        }
    }
    out
}

pub(crate) fn absolute_path(raw: &str) -> Result<PathBuf> {
    if raw.trim().is_empty() {
        bail!("Enter a path for the file action.");
    }
    let expanded = expand(raw);
    let path = Path::new(&expanded);
    let path = if path.is_absolute() {
        path.to_path_buf()
    } else {
        std::env::current_dir()?.join(path)
    };
    Ok(normalize_path(&path))
}

pub(crate) fn project_action_path(project: &ProjectCfg, raw: &str) -> Result<PathBuf> {
    let root = absolute_path(&project.dir)?;
    let path = absolute_path(raw)?;
    // Resolve existing ancestors as well as the leaf: new-file actions must not follow
    // a directory symlink out of the selected checkout.
    fn resolved(path: &Path) -> Result<PathBuf> {
        match path.canonicalize() {
            Ok(path) => Ok(path),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
                if path.is_symlink() {
                    return Err(error.into());
                }
                match (path.parent(), path.file_name()) {
                    (Some(parent), Some(name)) => Ok(resolved(parent)?.join(name)),
                    _ => Ok(path.to_path_buf()),
                }
            }
            Err(error) => Err(error.into()),
        }
    }
    if !resolved(&path)?.starts_with(resolved(&root)?) {
        bail!(
            "File action path must be inside project {:?}.",
            project.name
        );
    }
    Ok(path)
}

fn shell_quote(path: &str) -> String {
    format!("'{}'", path.replace('\'', "'\\''"))
}

pub(crate) fn hold_action_command(command: &str) -> String {
    format!(
        "bash -lc {}",
        shell_quote(&format!("{command}; exec \"${{SHELL:-bash}}\""))
    )
}

pub(crate) fn normalize_action_command(path: &Path, command: &str) -> String {
    let absolute = shell_quote(&path.to_string_lossy());
    command.replace("{{ absolute_path }}", &absolute)
}

#[cfg(test)]
#[path = "actions_tests.rs"]
mod tests;
