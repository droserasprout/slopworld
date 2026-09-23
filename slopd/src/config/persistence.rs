//! Config file loading, serialization, and redacted persistence.

use std::collections::HashSet;
use std::path::{Component, Path, PathBuf};
use std::time::SystemTime;

use anyhow::{bail, Context, Result};

use super::*;

impl Config {
    pub fn path() -> PathBuf {
        crate::paths::config_root().join("config.toml")
    }

    pub fn library_dirs_for(config_path: &Path) -> Vec<(LibraryItemKind, PathBuf)> {
        let root = config_path.parent().unwrap_or_else(|| Path::new("."));
        [
            LibraryItemKind::Prompt,
            LibraryItemKind::Breadcrumb,
            LibraryItemKind::FileAction,
            LibraryItemKind::Shell,
        ]
        .into_iter()
        .map(|kind| (kind, root.join(kind.config_dir())))
        .collect()
    }

    pub fn library_stamp_for(config_path: &Path) -> Option<SystemTime> {
        Self::library_dirs_for(config_path)
            .into_iter()
            .filter_map(|(_, dir)| crate::paths::dir_stamp(&dir))
            .max()
    }

    /// The configuration path, including any `SLOPD_CONFIG` override. `main` loads this file.
    /// `sandbox::refused` prevents bind mounts from exposing it because it contains the root token.
    /// An agent with this token can request a host terminal.
    pub fn path_in_use() -> PathBuf {
        std::env::var("SLOPD_CONFIG")
            .map(PathBuf::from)
            .unwrap_or_else(|_| Self::path())
    }

    pub async fn load(path: &Path) -> Result<Self> {
        if !tokio::fs::try_exists(path).await? {
            let cfg = Config::seed();
            cfg.save(path).await?;
            return Ok(cfg);
        }
        let text = tokio::fs::read_to_string(path)
            .await
            .with_context(|| format!("reading {}", path.display()))?;
        let (mut cfg, _) = Self::parse_document(&text)?;
        cfg.library = load_library(&Self::library_dirs_for(path)).await?;
        Ok(cfg)
    }

    #[cfg(test)]
    pub fn parse(text: &str) -> Result<Self> {
        let document: toml::Value = toml::from_str(text).context("parsing config.toml")?;
        reject_removed_worktree_fields(&document)?;
        if let Some(daemon) = document.get("daemon").and_then(toml::Value::as_table) {
            for key in ["usage", "openrouter", "openai"] {
                if daemon.contains_key(key) {
                    bail!("[daemon] {key} was removed");
                }
            }
        }
        let cfg: Self = document.try_into().context("parsing config.toml")?;
        super::validation::validate_loaded(&cfg)?;
        Ok(cfg)
    }

    /// Validate the current schema. Keep the original document to preserve unrelated fields and secrets during edits.
    /// Loading does not rewrite the configuration file.
    pub(crate) fn parse_document(text: &str) -> Result<(Self, toml::Value)> {
        let document: toml::Value = toml::from_str(text).context("parsing config.toml")?;
        reject_removed_worktree_fields(&document)?;
        if document.get("library").is_some() {
            bail!("inline library entries are no longer supported");
        }
        if let Some(daemon) = document.get("daemon").and_then(toml::Value::as_table) {
            for key in ["usage", "openrouter", "openai"] {
                if daemon.contains_key(key) {
                    bail!(
                        "The daemon no longer accepts [daemon] {key}. Configure usage rows under [daemon.usage_items.*]."
                    );
                }
            }
        }
        let cfg: Self = document.clone().try_into().context("parsing config.toml")?;
        super::validation::validate_loaded(&cfg)?;
        Ok((cfg, document))
    }

    pub async fn save(&self, path: &Path) -> Result<()> {
        super::validation::validate_loaded(self)?;
        let mut document = toml::Value::try_from(self)?;
        if tokio::fs::try_exists(path).await? {
            if let Ok(text) = tokio::fs::read_to_string(path).await {
                if let Ok(previous) = toml::from_str::<toml::Value>(&text) {
                    reject_removed_worktree_fields(&previous)?;
                    let previous_config: Self = previous.clone().try_into().context(
                        "reading modeled fields before preserving unknown configuration",
                    )?;
                    let previous_modeled = toml::Value::try_from(previous_config)?;
                    preserve_unknown_fields(&mut document, &previous, Some(&previous_modeled));
                }
            }
        }
        if let Some(table) = document.as_table_mut() {
            // Library entries have a separate catalog.
            // Do not serialize the in-memory catalog into the main configuration document.
            table.remove("library");
        }
        Self::save_text(path, &toml::to_string_pretty(&document)?).await?;
        save_library(&Self::library_dirs_for(path), &self.library).await
    }

    pub async fn save_text(path: &Path, text: &str) -> Result<()> {
        crate::paths::write_atomic_async(path, text, Some(0o600)).await
    }

    /// Create a copy for clients. Replace a nonempty token with the redaction sentinel.
    /// This prevents `GET /api/config` from exposing the secret.
    /// Keep an empty token to indicate that authentication is disabled.
    pub fn redacted(&self) -> Self {
        let mut c = self.clone();
        if !c.daemon.token.is_empty() {
            c.daemon.token = TOKEN_REDACTED.to_string();
        }
        c
    }

    fn seed() -> Self {
        Self {
            state_rules: vec![
                StateRule {
                    state: "waiting".into(),
                    pattern: r"(?i)(do you want|❯\s*1\.|yes, and don't ask again|press enter to continue)".into(),
                },
                StateRule {
                    state: "working".into(),
                    pattern: r"(?i)(esc to interrupt|to interrupt\))".into(),
                },
            ],
            ..Default::default()
        }
    }
}

fn validate_library_name(name: &str) -> Result<()> {
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

async fn load_library(dirs: &[(LibraryItemKind, PathBuf)]) -> Result<Vec<LibraryItemCfg>> {
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

async fn save_library(
    dirs: &[(LibraryItemKind, PathBuf)],
    library: &[LibraryItemCfg],
) -> Result<()> {
    let mut names = HashSet::new();
    for item in library {
        validate_library_name(&item.name)?;
        if !names.insert(item.name.clone()) {
            bail!("Declare library item {:?} only once.", item.name);
        }
    }

    let expected_paths: HashSet<PathBuf> = library
        .iter()
        .map(|item| {
            dirs.iter()
                .find(|(kind, _)| *kind == item.kind)
                .map(|(_, dir)| dir.join(format!("{}.toml", item.name)))
                .ok_or_else(|| anyhow::anyhow!("unknown library item kind {:?}", item.kind))
        })
        .collect::<Result<_>>()?;

    for (_, dir) in dirs {
        tokio::fs::create_dir_all(dir).await?;
        let mut entries = tokio::fs::read_dir(dir).await?;
        while let Some(entry) = entries.next_entry().await? {
            let path = entry.path();
            if path
                .extension()
                .is_some_and(|extension| extension == "toml")
                && !expected_paths.contains(&path)
            {
                tokio::fs::remove_file(&path).await?;
            }
        }
    }

    for item in library {
        let path = dirs
            .iter()
            .find(|(kind, _)| *kind == item.kind)
            .map(|(_, dir)| dir.join(format!("{}.toml", item.name)))
            .ok_or_else(|| anyhow::anyhow!("unknown library item kind {:?}", item.kind))?;
        let text = toml::to_string_pretty(item)?;
        crate::paths::write_atomic_async(&path, &text, Some(0o600)).await?;
    }
    Ok(())
}

// Reject retired names before unknown-field preservation can retain them as extensions.
fn reject_removed_worktree_fields(document: &toml::Value) -> Result<()> {
    for (section, old, replacement) in [
        ("project", "workspace_root", "worktree_root"),
        ("session", "workspace", "worktree"),
    ] {
        if document
            .get(section)
            .and_then(toml::Value::as_array)
            .is_some_and(|rows| rows.iter().any(|row| row.get(old).is_some()))
        {
            bail!("{section}.{old} was removed; use {replacement}");
        }
    }
    Ok(())
}

fn preserve_unknown_fields(
    modeled: &mut toml::Value,
    previous: &toml::Value,
    previous_modeled: Option<&toml::Value>,
) {
    match (modeled, previous) {
        (toml::Value::Table(modeled), toml::Value::Table(previous)) => {
            for (key, value) in previous {
                if let Some(current) = modeled.get_mut(key) {
                    preserve_unknown_fields(
                        current,
                        value,
                        previous_modeled.and_then(|v| v.get(key)),
                    );
                } else if previous_modeled.and_then(|v| v.get(key)).is_none() {
                    // A known non-default field omitted by the new serializer was cleared.
                    // Only fields absent from the old typed representation are extensions.
                    modeled.insert(key.clone(), value.clone());
                }
            }
        }
        (toml::Value::Array(modeled), toml::Value::Array(previous)) => {
            for current in modeled.iter_mut() {
                let Some(current_table) = current.as_table() else {
                    continue;
                };
                let Some(name) = current_table.get("name").and_then(toml::Value::as_str) else {
                    continue;
                };
                if let Some(previous_entry) = previous
                    .iter()
                    .find(|entry| entry.get("name").and_then(toml::Value::as_str) == Some(name))
                {
                    let old_model =
                        previous_modeled
                            .and_then(toml::Value::as_array)
                            .and_then(|entries| {
                                entries.iter().find(|entry| {
                                    entry.get("name").and_then(toml::Value::as_str) == Some(name)
                                })
                            });
                    preserve_unknown_fields(current, previous_entry, old_model);
                }
            }
        }
        _ => {}
    }
}

#[cfg(test)]
#[path = "persistence_tests.rs"]
mod tests;
