//! Config file loading, serialization, and redacted persistence.

use std::path::{Path, PathBuf};

use anyhow::{bail, Context, Result};

use super::*;

impl Config {
    pub fn path() -> PathBuf {
        dirs::config_dir()
            .unwrap_or_else(|| PathBuf::from("."))
            .join("slopworld/config.toml")
    }

    /// The file this daemon actually read, `SLOPD_CONFIG` included. `main` loads from it, and
    /// `sandbox::refused` keeps every bind list away from it: the token is in there, and an
    /// agent that can read it is an agent that can ask for a host terminal.
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
        Self::parse_document(&text).map(|(cfg, _)| cfg)
    }

    #[cfg(test)]
    pub fn parse(text: &str) -> Result<Self> {
        Self::parse_document(text).map(|(cfg, _)| cfg)
    }

    /// Validate the current schema while retaining the original document for edits that
    /// preserve unrelated fields and secrets. Loading never rewrites a configuration file.
    pub(crate) fn parse_document(text: &str) -> Result<(Self, toml::Value)> {
        let document: toml::Value = toml::from_str(text).context("parsing config.toml")?;
        if let Some(daemon) = document.get("daemon").and_then(toml::Value::as_table) {
            for key in ["usage", "openrouter", "openai"] {
                if daemon.contains_key(key) {
                    bail!(
                        "[daemon] {key} was removed; configure usage rows under [daemon.usage_items.*]"
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
                    let previous_config: Self = previous.clone().try_into().context(
                        "reading modeled fields before preserving unknown configuration",
                    )?;
                    let previous_modeled = toml::Value::try_from(previous_config)?;
                    preserve_unknown_fields(&mut document, &previous, Some(&previous_modeled));
                }
            }
        }
        Self::save_text(path, &toml::to_string_pretty(&document)?).await
    }

    pub async fn save_text(path: &Path, text: &str) -> Result<()> {
        crate::paths::write_atomic_async(path, text, Some(0o600)).await
    }

    /// A clone safe to hand a client: a set token becomes the sentinel, so `GET /api/config`
    /// never carries the secret to anything that reaches the endpoint. An empty token stays
    /// empty - "no auth" is a fact worth telling honestly, and there is nothing to leak.
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
mod tests {
    use super::*;

    const FIRST_ID: &str = "11111111-1111-4111-8111-111111111111";

    #[tokio::test]
    async fn clearing_modeled_settings_preserves_only_unknown_fields() {
        let root =
            std::env::temp_dir().join(format!("slopd-clear-settings-{}", uuid::Uuid::new_v4()));
        tokio::fs::create_dir_all(&root).await.unwrap();
        let path = root.join("config.toml");
        tokio::fs::write(
            &path,
            format!(
                r#"
            [daemon]
            bind = "127.0.0.1:7777"
            token = "secret"
            future_policy = "keep"
            [[project]]
            name = "repo"
            dir = "/tmp"
            [[session]]
            name = "agent"
            project = "repo"
            state_id = "{FIRST_ID}"
            sandbox = ["git"]
            persistent_tmp = true
            cmd = "old command"
            label = "old label"
            future_agent = "keep"
            [session.limits]
            memory_mb = 512
            pids = 100
        "#
            ),
        )
        .await
        .unwrap();
        let original = tokio::fs::read_to_string(&path).await.unwrap();
        let mut config = Config::load(&path).await.unwrap();
        assert_eq!(tokio::fs::read_to_string(&path).await.unwrap(), original);
        let agent = &mut config.sessions[0];
        agent.sandbox.clear();
        agent.persistent_tmp = false;
        agent.cmd = None;
        agent.label = None;
        agent.limits.memory_mb = None;
        config.save(&path).await.unwrap();
        let mut reloaded = Config::load(&path).await.unwrap();
        let agent = &reloaded.sessions[0];
        assert!(agent.sandbox.is_empty());
        assert!(!agent.persistent_tmp);
        assert!(agent.cmd.is_none() && agent.label.is_none());
        assert_eq!(agent.limits.memory_mb, None);
        assert_eq!(agent.limits.pids, Some(100));
        reloaded.sessions[0].limits = Limits::default();
        reloaded.save(&path).await.unwrap();
        assert!(Config::load(&path).await.unwrap().sessions[0]
            .limits
            .is_empty());
        let text = tokio::fs::read_to_string(&path).await.unwrap();
        assert!(text.contains("future_policy = \"keep\""));
        assert!(text.contains("future_agent = \"keep\""));
        assert_eq!(reloaded.daemon.token, "secret");
        tokio::fs::remove_dir_all(root).await.unwrap();
    }
}
