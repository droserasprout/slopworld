use std::net::Ipv4Addr;
use std::path::{Path, PathBuf};

use anyhow::{Context, Result};

use crate::config::{expand, Config, DnsConfig, NetworkMode, ProjectCfg, SessionCfg};
use crate::presets::SandboxPreset;

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
            if copy.exists() {
                continue;
            }
            let host_exists = Path::new(&host).exists();
            if host_exists {
                tracing::info!("session {:?} gets its own {host}", s.name);
                seed_into(pr, &host, &copy)?;
            } else if host.starts_with("/tmp/") {
                // The sandbox uses tmpfs for /tmp. This path has no host source.
                // Create an empty session-state directory for the bind mount in /tmp.
                tracing::info!("session {:?} gets a fresh {host}", s.name);
                std::fs::create_dir_all(&copy)
                    .with_context(|| format!("making {}", copy.display()))?;
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

/// Create a private copy from the host source if the copy does not exist.
/// Later host changes do not replace the agent's changes in an existing copy.
/// Delete the private copy to initialize it again.
pub(super) fn seed_into(pr: &SandboxPreset, host: &str, copy: &Path) -> Result<()> {
    if copy.exists() {
        return Ok(());
    }
    if let Some(parent) = copy.parent() {
        std::fs::create_dir_all(parent).with_context(|| format!("making {}", parent.display()))?;
    }

    // A file is its own seed.
    if Path::new(host).is_file() {
        std::fs::copy(host, copy).with_context(|| format!("seeding {}", copy.display()))?;
        return Ok(());
    }
    std::fs::create_dir_all(copy).with_context(|| format!("making {}", copy.display()))?;

    // Expand `skip` once before traversal. Also exclude shared files, which use host bind mounts.
    // Copying shared credentials here could leave obsolete credentials in the session directory.
    let skip: Vec<String> = pr
        .skip
        .iter()
        .chain(pr.shared.iter())
        .map(|s| expand(s))
        .collect();
    let skipped = |p: &Path| skip.iter().any(|s| Path::new(s) == p);

    // Copy top-level files without tool-specific rules.
    // `skip` excludes sensitive history and shared files.
    match std::fs::read_dir(host) {
        Ok(entries) => {
            for entry in entries.flatten() {
                if entry.path().is_file() && !skipped(&entry.path()) {
                    let to = copy.join(entry.file_name());
                    if let Err(e) = std::fs::copy(entry.path(), &to) {
                        tracing::warn!("seeding {}: {e:#}", to.display());
                    }
                }
            }
        }
        Err(e) => tracing::warn!("reading {host}: {e:#}"),
    }

    // Copy the subdirectories that the preset names, such as agents, commands, and plugins.
    // A user preset can specify additional state for one project or agent.
    for from in &pr.seed {
        let from = expand(from);
        let Ok(rel) = Path::new(&from).strip_prefix(host) else {
            continue; // a seed for some other private path, or for another preset's
        };
        let to = copy.join(rel);
        // Log missing sources and successful copies.
        // A missing source can indicate optional software state or an incorrect preset path.
        if !Path::new(&from).exists() {
            tracing::info!("seed {from} is not on this machine, nothing copied");
            continue;
        }
        match seed(Path::new(&from), &to, &skip) {
            Ok(()) => tracing::info!("seeded {from}"),
            Err(e) => tracing::warn!("seeding {} from {from}: {e:#}", to.display()),
        }
    }
    Ok(())
}

/// Recursively copy an existing source entry, except paths in `skip`.
/// Permit missing sources because presets describe optional software state.
fn seed(from: &Path, to: &Path, skip: &[String]) -> Result<()> {
    if !from.exists() || skip.iter().any(|s| Path::new(s) == from) {
        return Ok(());
    }
    if from.is_file() {
        if let Some(parent) = to.parent() {
            std::fs::create_dir_all(parent)?;
        }
        std::fs::copy(from, to)?;
        return Ok(());
    }
    std::fs::create_dir_all(to)?;
    for entry in std::fs::read_dir(from)? {
        let entry = entry?;
        seed(&entry.path(), &to.join(entry.file_name()), skip)?;
    }
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
