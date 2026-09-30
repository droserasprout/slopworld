//! Resolver preparation and launch-time DNS selection; private-copy seeding belongs to seed.

use std::net::Ipv4Addr;
use std::path::PathBuf;

use anyhow::{Context, Result};

use crate::config::{expand, Config, DnsConfig, NetworkMode, ProjectCfg, SessionCfg};

use super::seed::seed_into;
use super::{persistent_tmp_path, presets_for, private_path, state_root, PRIVATE_RESOLVER};

/// Create resolver files and initialize private copies before argument construction.
/// Keep existing private copies. Private network mode always uses a generated resolver file.
/// Host network mode also needs this file when the configuration specifies DNS servers.
pub fn prepare_network(cfg: &Config, s: &SessionCfg, p: &ProjectCfg) -> Result<()> {
    if s.persistent_tmp {
        let path = persistent_tmp_path(s)?;
        if path.exists() && !path.is_dir() {
            anyhow::bail!("persistent /tmp path {} is not a directory", path.display());
        }
        std::fs::create_dir_all(&path)
            .with_context(|| format!("making persistent /tmp {}", path.display()))?;
    }

    let network = cfg.network_of(s, p);
    let dns = cfg.dns_of(s, p);
    match network {
        NetworkMode::Private => prepare_resolver(&s.state_id, &[PRIVATE_RESOLVER.into()])?,
        NetworkMode::Host if matches!(dns, DnsConfig::Servers { .. }) => {
            prepare_resolver(&s.state_id, &dns_servers(&dns))?
        }
        NetworkMode::None | NetworkMode::Host => {}
    }

    // Use the same saved preset definitions for preparation and launch-plan construction.
    // A preset can change from private copies to shared mounts.
    // Different tables can leave a session without either the initial copy or the shared mount.
    let t = s.preset_table();
    for pr in presets_for(cfg, s, p, &t)? {
        for path in &pr.private {
            let host = expand(path);
            if host.is_empty() {
                continue;
            }
            let copy = private_path(&s.state_id, &host)?;
            if std::fs::symlink_metadata(&copy).is_ok() {
                continue;
            }
            match std::fs::symlink_metadata(&host) {
                Ok(_) => {
                    tracing::info!("session {:?} gets its own {host}", s.name);
                    seed_into(pr, &host, &copy)?;
                }
                Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
                    if host.starts_with("/tmp/") {
                        // The sandbox uses tmpfs for /tmp. This path has no host source.
                        // Create an empty session-state directory for the bind mount in /tmp.
                        tracing::info!("session {:?} gets a fresh {host}", s.name);
                        std::fs::create_dir_all(&copy)
                            .with_context(|| format!("making {}", copy.display()))?;
                    }
                }
                Err(error) => {
                    return Err(error).with_context(|| format!("reading private source {host}"))
                }
            }
        }
    }
    Ok(())
}

pub(crate) fn private_resolver_path(session: &str) -> Result<PathBuf> {
    Ok(state_root()
        .join(crate::config::state_id_component(session)?)
        .join("network/resolv.conf"))
}

fn prepare_resolver(session: &str, servers: &[String]) -> Result<()> {
    let path = private_resolver_path(session)?;
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent).with_context(|| format!("making {}", parent.display()))?;
    }
    let text = servers
        .iter()
        .map(|server| format!("nameserver {server}"))
        .collect::<Vec<_>>()
        .join("\n");
    std::fs::write(&path, format!("{text}\n"))
        .with_context(|| format!("writing {}", path.display()))?;
    Ok(())
}

/// Resolve configured DNS servers against the host resolver at launch time.
pub(crate) fn dns_servers(dns: &DnsConfig) -> Vec<String> {
    match dns {
        DnsConfig::Resolved => system_resolvers(),
        DnsConfig::Servers { servers } => servers.iter().map(ToString::to_string).collect(),
    }
}

fn system_resolvers() -> Vec<String> {
    std::fs::read_to_string("/etc/resolv.conf")
        .ok()
        .map(|text| resolvers_from(&text))
        .filter(|servers| !servers.is_empty())
        .unwrap_or_else(|| vec!["127.0.0.53".into()])
}

fn resolvers_from(text: &str) -> Vec<String> {
    let mut out = Vec::new();
    for line in text.lines() {
        let mut fields = line.split_whitespace();
        if fields.next() != Some("nameserver") {
            continue;
        }
        let Some(value) = fields.next() else { continue };
        let Ok(server) = value.parse::<Ipv4Addr>() else {
            continue;
        };
        if server.is_unspecified() || server.is_multicast() {
            continue;
        }
        let server = server.to_string();
        if !out.contains(&server) {
            out.push(server);
        }
        if out.len() == 2 {
            break;
        }
    }
    out
}

#[cfg(test)]
#[path = "network_tests.rs"]
mod tests;
