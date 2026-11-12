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

impl ConnectionSource {
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
        let endpoint = Url::parse(&url).context("Invalid daemon URL")?;
        if !matches!(endpoint.scheme(), "http" | "https")
            || endpoint.host_str().is_none()
            || !endpoint.username().is_empty()
            || endpoint.password().is_some()
            || token.is_empty()
        {
            bail!("Invalid daemon connection");
        }
        let mut token = axum::http::HeaderValue::from_str(&token)?;
        token.set_sensitive(true);
        Ok(Connection { endpoint, token })
    }
}
