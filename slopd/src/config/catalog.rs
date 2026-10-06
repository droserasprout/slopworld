//! Per-kind library file validation, loading, and save preparation.
//! The transaction owner commits prepared files with the main document.
use super::{LibraryItemCfg, LibraryItemKind};
use anyhow::{Context, Result, bail};
use std::collections::HashSet;
use std::path::{Component, Path, PathBuf};

pub(super) fn validate_library_name(name: &str) -> Result<()> {
    if name.trim().is_empty() || name == "." || name == ".." {
        bail!("library item name must be a file name");
    }
    if name.bytes().any(|b| b == b'/' || b == b'\\' || b == 0)
        || name.chars().any(|c| c.is_control())
    {
        bail!("library item name must be one safe file name");
    }
    match (
        Path::new(name).components().next(),
        Path::new(name).components().nth(1),
    ) {
        (Some(Component::Normal(_)), None) => Ok(()),
        _ => bail!("library item name must be one safe file name"),
    }
}

pub(super) async fn load_library(
    dirs: &[(LibraryItemKind, PathBuf)],
) -> Result<Vec<LibraryItemCfg>> {
    let mut paths = Vec::new();
    for (kind, dir) in dirs {
        let mut entries = match tokio::fs::read_dir(dir).await {
            Ok(entries) => entries,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => continue,
            Err(error) => return Err(error).with_context(|| format!("reading {}", dir.display())),
        };
        while let Some(entry) = entries.next_entry().await? {
            let path = entry.path();
            if path
                .extension()
                .is_some_and(|extension| extension == "toml")
            {
                paths.push((*kind, path));
            }
        }
    }
    paths.sort_by(|(_, a), (_, b)| a.cmp(b));

    let mut names = HashSet::new();
    let mut library = Vec::with_capacity(paths.len());
    for (kind, path) in paths {
        let text = tokio::fs::read_to_string(&path)
            .await
            .with_context(|| format!("reading library item {}", path.display()))?;
        let mut item: LibraryItemCfg = toml::from_str(&text)
            .with_context(|| format!("parsing library item {}", path.display()))?;
        item.builtin = false;
        if item.kind != kind {
            bail!(
                "library item file {} has kind {:?}, but belongs in {:?}",
                path.display(),
                item.kind,
                kind.config_dir()
            );
        }
        validate_library_name(&item.name)
            .with_context(|| format!("library item in {}", path.display()))?;
        let expected = path
            .file_stem()
            .and_then(|stem| stem.to_str())
            .unwrap_or_default();
        if expected != item.name {
            bail!(
                "library item file {} names {:?}, expected {:?}",
                path.display(),
                item.name,
                expected
            );
        }
        if !names.insert(item.name.clone()) {
            bail!("Declare library item {:?} only once.", item.name);
        }
        library.push(item);
    }
    Ok(library)
}

pub(crate) fn prepare_library(
    dirs: &[(LibraryItemKind, PathBuf)],
    library: &[LibraryItemCfg],
) -> Result<Vec<(PathBuf, String)>> {
    let mut names = HashSet::new();
    library
        .iter()
        .map(|item| {
            validate_library_name(&item.name)?;
            if !names.insert(&item.name) {
                bail!("Declare library item {:?} only once.", item.name);
            }
            let dir = dirs
                .iter()
                .find(|(kind, _)| *kind == item.kind)
                .map(|(_, dir)| dir)
                .ok_or_else(|| anyhow::anyhow!("unknown library item kind {:?}", item.kind))?;
            Ok((
                dir.join(format!("{}.toml", item.name)),
                toml::to_string_pretty(item)?,
            ))
        })
        .collect()
}

/// A deliberate whole-catalog replacement selects retirement here, not in the
/// transaction owner. Incremental callers can prepare their own targeted changes.
pub(crate) async fn replacement_changes(
    dirs: &[(LibraryItemKind, PathBuf)],
    prepared: Vec<(PathBuf, String)>,
) -> Result<std::collections::BTreeMap<PathBuf, Option<String>>> {
    let mut changes: std::collections::BTreeMap<_, _> = prepared
        .into_iter()
        .map(|(path, text)| (path, Some(text)))
        .collect();
    for (_, dir) in dirs {
        let mut entries = match tokio::fs::read_dir(dir).await {
            Ok(entries) => entries,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => continue,
            Err(error) => return Err(error.into()),
        };
        while let Some(entry) = entries.next_entry().await? {
            let target = entry.path();
            if target.extension().is_some_and(|ext| ext == "toml") {
                changes.entry(target).or_insert(None);
            }
        }
    }
    Ok(changes)
}

/// Membership and every file revision participate; a newer sibling cannot mask
/// an edit or deletion. Failure leaves the accepted catalog/revision unchanged.
pub(crate) type Revision = Vec<(PathBuf, u64, std::time::SystemTime)>;

pub(crate) fn revision(config: &Path) -> Result<Revision> {
    let mut result = Vec::new();
    for (_, directory) in super::Config::library_dirs_for(config) {
        let entries = match std::fs::read_dir(directory) {
            Ok(entries) => entries,
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => continue,
            Err(e) => return Err(e.into()),
        };
        for entry in entries {
            let path = entry?.path();
            if path.extension().is_some_and(|ext| ext == "toml") {
                let metadata = std::fs::metadata(&path)?;
                result.push((path, metadata.len(), metadata.modified()?));
            }
        }
    }
    result.sort();
    Ok(result)
}
