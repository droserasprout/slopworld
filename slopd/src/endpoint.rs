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

/// Update only the secret after a live config edit. The listener's address is fixed until
/// restart, so changing `[daemon] bind` must not make the descriptor advertise an address the
/// current process is not listening on.
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
    let tmp = path.with_extension("toml.tmp");
    tokio::fs::write(&tmp, text)
        .await
        .with_context(|| format!("writing {}", tmp.display()))?;

    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        tokio::fs::set_permissions(&tmp, std::fs::Permissions::from_mode(0o600)).await?;
    }

    tokio::fs::rename(&tmp, path)
        .await
        .with_context(|| format!("installing endpoint descriptor {}", path.display()))?;
    Ok(())
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
mod tests {
    use super::{update_token, url_for, write_endpoint, Endpoint};

    #[test]
    fn loopback_url_is_written_for_wildcard_binds() {
        assert_eq!(url_for("0.0.0.0:7717"), "http://127.0.0.1:7717");
        assert_eq!(url_for("[::]:7717"), "http://[::1]:7717");
    }

    #[test]
    fn ipv6_urls_are_bracketed() {
        assert_eq!(url_for("[::1]:7717"), "http://[::1]:7717");
        assert_eq!(url_for("127.0.0.1:9000"), "http://127.0.0.1:9000");
        assert_eq!(url_for("localhost:7717"), "http://localhost:7717");
    }

    #[test]
    fn descriptor_round_trips_through_toml() {
        let endpoint = Endpoint {
            url: "http://127.0.0.1:7717".into(),
            token: "quotes \" and slash \\".into(),
        };
        let text = toml::to_string_pretty(&endpoint).unwrap();
        let back: Endpoint = toml::from_str(&text).unwrap();
        assert_eq!(back.url, endpoint.url);
        assert_eq!(back.token, endpoint.token);
    }

    #[tokio::test]
    async fn descriptor_writes_and_token_updates_preserve_the_bound_url() {
        let path = std::env::temp_dir().join(format!(
            "slopd-endpoint-{}-{}.toml",
            std::process::id(),
            uuid::Uuid::new_v4()
        ));
        let endpoint = Endpoint {
            url: "http://127.0.0.1:7717".into(),
            token: "old".into(),
        };
        write_endpoint(&path, &endpoint).await.unwrap();

        update_token(&path, "new").await.unwrap();

        let written: Endpoint = toml::from_str(&std::fs::read_to_string(&path).unwrap()).unwrap();
        assert_eq!(written.url, endpoint.url);
        assert_eq!(written.token, "new");
        std::fs::remove_file(path).unwrap();
    }
}
