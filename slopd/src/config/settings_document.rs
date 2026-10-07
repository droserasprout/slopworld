//! Root-only document preparation for the record layout. No disk or publication.
//! The manager must validate the assembled candidate and commit before publication.

use super::Settings;
use crate::config::{Config, TOKEN_REDACTED};
use anyhow::{Context, Result, bail};

pub(crate) struct PreparedSettings {
    pub(crate) candidate: Config,
    pub(crate) text: String,
}

/// Replacement owns settings and extensions, never workspace membership. The
/// caller holds the persistence gate and supplies its accepted workspace view.
pub(crate) fn replace(old: &Config, text: &str) -> Result<PreparedSettings> {
    let document = toml::from_str(text).context("parsing root settings")?;
    prepare(old, document)
}

/// JSON-to-TOML conversion remains at the API boundary. Explicit false, zero,
/// empty strings and arrays are values; only absent keys mean unchanged.
pub(crate) fn patch(old: &Config, text: &str, patch: toml::Value) -> Result<PreparedSettings> {
    reject_workspace(&patch)?;
    let previous = toml::from_str(text).context("parsing root settings")?;
    reject_workspace(&previous)?;
    // A valid sparse root can omit entire defaulted sections. Materialize the
    // accepted defaults before adding a leaf to such a section.
    let mut document = toml::Value::try_from(&old.settings)?;
    merge(&mut document, previous);
    merge(&mut document, patch);
    prepare(old, document)
}

fn prepare(old: &Config, mut document: toml::Value) -> Result<PreparedSettings> {
    reject_workspace(&document)?;
    if let Some(daemon) = document.get("daemon").and_then(toml::Value::as_table) {
        for key in ["usage", "openrouter", "openai"] {
            if daemon.contains_key(key) {
                bail!("[daemon] {key} was removed");
            }
        }
    }
    let mut settings: Settings = document
        .clone()
        .try_into()
        .context("decoding root settings")?;
    if settings.daemon.token == TOKEN_REDACTED {
        settings.daemon.token = old.daemon.token.clone();
        document
            .get_mut("daemon")
            .and_then(toml::Value::as_table_mut)
            .context("missing daemon table")?
            .insert(
                "token".into(),
                toml::Value::String(settings.daemon.token.clone()),
            );
    }
    let mut candidate = old.clone();
    candidate.settings = settings;
    Ok(PreparedSettings {
        candidate,
        text: toml::to_string_pretty(&document)?,
    })
}

fn reject_workspace(document: &toml::Value) -> Result<()> {
    let table = document
        .as_table()
        .context("root settings must be a table")?;
    for key in ["project", "session", "host_terminal", "library"] {
        if table.contains_key(key) {
            bail!("inline {key} is not supported in root settings; use its owning API");
        }
    }
    Ok(())
}

fn merge(base: &mut toml::Value, patch: toml::Value) {
    match (base, patch) {
        (toml::Value::Table(base), toml::Value::Table(patch)) => {
            for (key, value) in patch {
                if let Some(existing) = base.get_mut(&key) {
                    merge(existing, value);
                } else {
                    base.insert(key, value);
                }
            }
        }
        (base, value) => *base = value,
    }
}
