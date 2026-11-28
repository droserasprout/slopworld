//! Fresh host-only connection snapshots. Failed reads never reuse old credentials.
use std::path::PathBuf;

use anyhow::{Context, Result, bail};
use serde::Deserialize;
use url::Url;

#[derive(Clone)]
pub enum ConnectionSource {
    Descriptor(PathBuf),
    Direct {
        url: String,
        token: Option<String>,
        token_file: Option<PathBuf>,
    },
    Unconfigured,
}

impl std::fmt::Debug for ConnectionSource {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::Descriptor(path) => formatter.debug_tuple("Descriptor").field(path).finish(),
            Self::Direct { token_file, .. } => formatter
                .debug_struct("Direct")
                .field("url", &"[host endpoint]")
                .field("token", &"[redacted]")
                .field("token_file", token_file)
                .finish(),
            Self::Unconfigured => formatter.write_str("Unconfigured"),
        }
    }
}

pub(super) struct Connection {
    pub endpoint: Url,
    pub token: axum::http::HeaderValue,
}

#[derive(Deserialize)]
struct Descriptor {
    url: String,
    token: String,
}

/// Keep a directory base so relative protocol routes retain the daemon's mount path.
fn daemon_directory(value: &str) -> Result<Url> {
    let mut url = Url::parse(value).context("Invalid daemon URL")?;
    if !matches!(url.scheme(), "http" | "https")
        || url.host_str().is_none()
        || !url.username().is_empty()
        || url.password().is_some()
        || url.query().is_some()
        || url.fragment().is_some()
    {
        bail!("Expected an HTTP or HTTPS daemon URL without credentials, query, or fragment");
    }
    url.set_path(&format!("{}/", url.path().trim_end_matches('/')));
    Ok(url)
}

impl ConnectionSource {
    pub(crate) async fn validate(&self) -> Result<()> {
        self.snapshot().await.map(drop)
    }

    pub(super) async fn snapshot(&self) -> Result<Connection> {
        let (url, token) = match self {
            Self::Descriptor(path) => {
                let descriptor: Descriptor =
                    toml::from_str(&tokio::fs::read_to_string(path).await?)?;
                (descriptor.url, descriptor.token)
            }
            Self::Direct {
                url,
                token,
                token_file,
            } => {
                let token = if let Some(path) = token_file {
                    tokio::fs::read_to_string(path).await?.trim().to_owned()
                } else {
                    token.clone().unwrap_or_default()
                };
                (url.clone(), token)
            }
            Self::Unconfigured => bail!("Host endpoint unavailable"),
        };
        let endpoint = daemon_directory(&url)?;
        if token.is_empty() {
            bail!("Invalid daemon connection");
        }
        let mut token = axum::http::HeaderValue::from_str(&token)?;
        token.set_sensitive(true);
        Ok(Connection { endpoint, token })
    }
}
