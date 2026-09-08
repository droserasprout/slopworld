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
        Self::parse(&text)
    }

    pub fn parse(text: &str) -> Result<Self> {
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
        let cfg: Self = document.try_into().context("parsing config.toml")?;
        super::validation::validate_loaded(&cfg)?;
        Ok(cfg)
    }

    pub async fn save(&self, path: &Path) -> Result<()> {
        super::validation::validate_loaded(self)?;
        Self::save_text(path, &toml::to_string_pretty(self)?).await
    }

    pub async fn save_text(path: &Path, text: &str) -> Result<()> {
        if let Some(parent) = path.parent() {
            tokio::fs::create_dir_all(parent).await?;
        }

        let tmp = path.with_extension("toml.tmp");
        tokio::fs::write(&tmp, text).await?;
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            tokio::fs::set_permissions(&tmp, std::fs::Permissions::from_mode(0o600)).await?;
        }
        tokio::fs::rename(tmp, path).await?;
        Ok(())
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
