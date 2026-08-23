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

pub fn write(bind: &str, token: &str) -> Result<()> {
    let path = path();
    write_endpoint(
        &path,
        &Endpoint {
            url: url_for(bind),
            token: token.to_string(),
        },
    )?;
    Ok(())
}

/// Update only the secret after a live config edit. The listener's address is fixed until
/// restart, so changing `[daemon] bind` must not make the descriptor advertise an address the
/// current process is not listening on.
pub fn update_token(token: &str) -> Result<()> {
    let path = path();
    if !path.exists() {
        return Ok(());
    }
    let text = std::fs::read_to_string(&path)
        .with_context(|| format!("reading endpoint descriptor {}", path.display()))?;
    let mut endpoint: Endpoint = toml::from_str(&text)
        .with_context(|| format!("parsing endpoint descriptor {}", path.display()))?;
    endpoint.token = token.to_string();
    write_endpoint(&path, &endpoint)
}

pub fn remove() {
    let _ = std::fs::remove_file(path());
}

fn write_endpoint(path: &Path, endpoint: &Endpoint) -> Result<()> {
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent)?;
    }

    let text = toml::to_string_pretty(endpoint)?;
    let tmp = path.with_extension("toml.tmp");
    std::fs::write(&tmp, text).with_context(|| format!("writing {}", tmp.display()))?;

    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        std::fs::set_permissions(&tmp, std::fs::Permissions::from_mode(0o600))?;
    }

    std::fs::rename(&tmp, path)
        .with_context(|| format!("installing endpoint descriptor {}", path.display()))?;
    Ok(())
}

fn url_for(bind: &str) -> String {
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
    use super::{url_for, Endpoint};

    #[test]
    fn loopback_url_is_written_for_wildcard_binds() {
        assert_eq!(url_for("0.0.0.0:7717"), "http://127.0.0.1:7717");
        assert_eq!(url_for("[::]:7717"), "http://[::1]:7717");
    }

    #[test]
    fn ipv6_urls_are_bracketed() {
        assert_eq!(url_for("[::1]:7717"), "http://[::1]:7717");
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
}
