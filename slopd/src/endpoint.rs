use std::net::SocketAddr;
use std::path::{Path, PathBuf};

use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Endpoint {
    pub url: String,
    pub token: String,
}

pub fn path() -> PathBuf {
    std::env::var("SLOPD_ENDPOINT")
        .map(PathBuf::from)
        .unwrap_or_else(|_| {
            dirs::config_dir()
                .unwrap_or_else(|| PathBuf::from("."))
                .join("slopworld/endpoint.toml")
        })
}

pub async fn write(bind: &str, token: &str) -> Result<()> {
    let path = path();
    write_endpoint(
        &path,
        &Endpoint {
            url: url_for(bind),
            token: token.to_string(),
        },
    )
    .await?;
    Ok(())
}

/// Update only the token after a live configuration change.
/// The listener keeps its address until restart.
/// A change to `[daemon] bind` must not make the descriptor report an address that the current process does not use.
pub async fn update_token(path: &Path, token: &str) -> Result<()> {
    if !tokio::fs::try_exists(path).await? {
        return Ok(());
    }
    let text = tokio::fs::read_to_string(path)
        .await
        .with_context(|| format!("reading endpoint descriptor {}", path.display()))?;
    let mut endpoint: Endpoint = toml::from_str(&text)
        .with_context(|| format!("parsing endpoint descriptor {}", path.display()))?;
    endpoint.token = token.to_string();
    write_endpoint(path, &endpoint).await
}

pub fn remove() {
    let _ = std::fs::remove_file(path());
}

async fn write_endpoint(path: &Path, endpoint: &Endpoint) -> Result<()> {
    if let Some(parent) = path.parent() {
        tokio::fs::create_dir_all(parent).await?;
    }

    let text = toml::to_string_pretty(endpoint)?;
    crate::paths::write_atomic_async(path, &text, Some(0o600))
        .await
        .with_context(|| format!("installing endpoint descriptor {}", path.display()))
}

pub(crate) fn url_for(bind: &str) -> String {
    let Ok(addr) = bind.parse::<SocketAddr>() else {
        return format!("http://{bind}");
    };

    let ip = if addr.ip().is_unspecified() {
        if addr.is_ipv4() {
            "127.0.0.1".to_string()
        } else {
            "[::1]".to_string()
        }
    } else if addr.is_ipv6() {
        format!("[{}]", addr.ip())
    } else {
        addr.ip().to_string()
    };
    format!("http://{ip}:{}", addr.port())
}

#[cfg(test)]
#[path = "endpoint_tests.rs"]
mod tests;
