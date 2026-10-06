//! Config file loading, serialization, and redacted persistence.

use super::catalog::load_library;
use std::path::{Path, PathBuf};
use std::time::SystemTime;

use anyhow::{Context, Result, bail};

use super::*;

/// Protocol sentinel for a redacted token. Writing this value preserves the token.
/// Writing a new value or an empty string changes the token.
pub const TOKEN_REDACTED: &str = "<redacted>";

/// Redact a nonempty `[daemon] token` in configuration text. Preserve comments and blank lines.
/// `Manager::replace_config` restores the real value when a client writes the sentinel.
pub fn redact_token_text(text: &str) -> Result<String> {
    let mut document: toml_edit::DocumentMut =
        text.parse().context("parsing config for token redaction")?;
    if let Some(token) = document
        .get_mut("daemon")
        .and_then(|daemon| daemon.get_mut("token"))
    {
        let value = token
            .as_value()
            .context("[daemon] token must be a string")?;
        let secret = value.as_str().context("[daemon] token must be a string")?;
        if !secret.is_empty() {
            let decor = value.decor().clone();
            *token = toml_edit::value(TOKEN_REDACTED);
            if let Some(value) = token.as_value_mut() {
                *value.decor_mut() = decor;
            }
        }
    }
    Ok(document.to_string())
}

impl Config {
    /// Private undo journal, containing the previous configuration token.
    /// Sandbox path guards protect it alongside the main configuration.
    pub(crate) fn recovery_path_for(path: &Path) -> PathBuf {
        super::transaction::journal_path(path)
    }

    pub fn library_dirs_for(config_path: &Path) -> Vec<(LibraryItemKind, PathBuf)> {
        let root = config_path
            .parent()
            .filter(|p| !p.as_os_str().is_empty())
            .unwrap_or_else(|| Path::new("."));
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

    pub(crate) async fn load_library_for(path: &Path) -> Result<Vec<LibraryItemCfg>> {
        load_library(&Self::library_dirs_for(path)).await
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
        crate::paths::config_file()
    }

    pub async fn load(path: &Path) -> Result<Self> {
        super::transaction::recover(path).await?;
        if !tokio::fs::try_exists(path).await? {
            let cfg = Config {
                library: Self::load_library_for(path).await?,
                ..Default::default()
            };
            super::legacy::save(&cfg, path).await?;
            return Ok(cfg);
        }
        let text = tokio::fs::read_to_string(path)
            .await
            .with_context(|| format!("reading {}", path.display()))?;
        let (mut cfg, _) = Self::parse_document(&text)?;
        cfg.library = Self::load_library_for(path).await?;
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

    pub async fn save_text(path: &Path, text: &str) -> Result<()> {
        super::transaction::recover(path).await?;
        crate::paths::write_atomic_async(path, text, Some(0o600)).await
    }

    /// Prepare a deletion only from the accepted disk revision. Recover first,
    /// then sample on both sides of the read so unseen edits use the normal path.
    pub(crate) async fn session_removal_text(
        path: &Path,
        name: &str,
        accepted: Option<SystemTime>,
    ) -> Result<Option<String>> {
        super::transaction::recover(path).await?;
        if accepted.is_none() || crate::paths::disk_mtime(path).await != accepted {
            return Ok(None);
        }
        let text = tokio::fs::read_to_string(path).await?;
        if crate::paths::disk_mtime(path).await != accepted {
            return Ok(None);
        }
        let mut document: toml_edit::DocumentMut = text.parse()?;
        // Typed saves emit arrays of tables. Alternate valid encodings retain
        // the generic save path rather than gaining another mutation policy.
        let Some(sessions) = document
            .get_mut("session")
            .and_then(toml_edit::Item::as_array_of_tables_mut)
        else {
            return Ok(None);
        };
        let before = sessions.len();
        sessions
            .retain(|session| session.get("name").and_then(toml_edit::Item::as_str) != Some(name));
        if sessions.len() == before {
            return Ok(None);
        }
        Ok(Some(document.to_string()))
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
}

// Reject retired names before unknown-field preservation can retain them as extensions.
pub(super) fn reject_removed_worktree_fields(document: &toml::Value) -> Result<()> {
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

pub(super) fn preserve_unknown_fields(
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
            // Named arrays include every configured worker. Match each old
            // document once, preserving the former first-match behavior.
            let previous = named_entries(previous);
            let previous_modeled = named_entries(
                previous_modeled
                    .and_then(toml::Value::as_array)
                    .map(Vec::as_slice)
                    .unwrap_or_default(),
            );
            for current in modeled.iter_mut() {
                let Some(current_table) = current.as_table() else {
                    continue;
                };
                let Some(name) = current_table.get("name").and_then(toml::Value::as_str) else {
                    continue;
                };
                if let Some(previous_entry) = previous.get(name) {
                    let old_model = previous_modeled.get(name).copied();
                    preserve_unknown_fields(current, previous_entry, old_model);
                }
            }
        }
        _ => {}
    }
}

fn named_entries(entries: &[toml::Value]) -> std::collections::HashMap<&str, &toml::Value> {
    let mut index = std::collections::HashMap::with_capacity(entries.len());
    for entry in entries {
        if let Some(name) = entry.get("name").and_then(toml::Value::as_str) {
            index.entry(name).or_insert(entry);
        }
    }
    index
}

#[cfg(test)]
#[path = "persistence_tests.rs"]
mod tests;
