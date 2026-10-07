//! Config file loading, serialization, and redacted persistence.

use super::catalog::load_library;
use std::path::{Path, PathBuf};

#[cfg(test)]
use anyhow::bail;
use anyhow::{Context, Result};

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
    /// Retired undo journal path, retained for layout rejection and sandbox protection.
    /// Sandbox path guards protect it alongside the main configuration.
    pub(crate) fn recovery_path_for(path: &Path) -> PathBuf {
        let mut name = path.file_name().unwrap_or_default().to_os_string();
        name.push(".save-journal");
        path.with_file_name(name)
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

    /// The configuration path, including any `SLOPD_CONFIG` override. `main` loads this file.
    /// `sandbox::refused` prevents bind mounts from exposing it because it contains the root token.
    /// An agent with this token can request a host terminal.
    pub fn path_in_use() -> PathBuf {
        crate::paths::config_file()
    }

    #[cfg(test)]
    pub async fn load(path: &Path) -> Result<Self> {
        let binding = super::fixtures::binding(path)?;
        Ok(crate::storage::layout::load(&binding).await?.config)
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
#[cfg(test)]
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

#[cfg(test)]
#[path = "persistence_tests.rs"]
mod tests;
