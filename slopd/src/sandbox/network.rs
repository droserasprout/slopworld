use std::path::{Path, PathBuf};

use anyhow::{Context, Result};

use crate::config::{expand, Config, DnsConfig, NetworkMode, ProjectCfg, SessionCfg};
use crate::presets::SandboxPreset;

use super::{persistent_tmp_path, presets_for, private_path, state_root, PRIVATE_RESOLVER};

/// Creates generated resolver files and seeds each private tree before argv construction;
/// existing copies are preserved. The private resolver is always synthetic, while an explicit
/// DNS list in host mode needs a generated `/etc/resolv.conf` source too.
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
            prepare_resolver(&s.state_id, &dns.servers())?
        }
        NetworkMode::None | NetworkMode::Host => {}
    }

    // Keep preparation on the same captured definitions as launch-plan construction. A live
    // preset can change from seeded state to a shared bind (Codex auth did); mixing the tables
    // leaves a snapshot session with neither the old seed nor the new mount.
    let t = s.preset_table();
    for pr in presets_for(cfg, s, p, &t) {
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
                // /tmp is a tmpfs in the skeleton, so the host path never exists. Create an
                // empty session-state directory and let the bind land on the tmpfs mount point.
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

/// One private path, made and seeded. Returns having done nothing if the copy is already
/// there, which is what "once" means: what an agent has written is never trodden on by what
/// the host has changed since. The tree is an ordinary directory - deleting a session's is
/// how it is handed a fresh one.
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

    // `skip` is expanded once here rather than per directory walked: it is a handful of paths
    // and the walk is not. A shared file is skipped too, and not because of its size: it is
    // bound over from the host anyway, and seeding it would leave a superseded credential
    // lying in the session directory for as long as that session exists.
    let skip: Vec<String> = pr
        .skip
        .iter()
        .chain(pr.shared.iter())
        .map(|s| expand(s))
        .collect();
    let skipped = |p: &Path| skip.iter().any(|s| Path::new(s) == p);

    // Copy top-level files generically for tool portability; `skip` also excludes sensitive
    // history and shared files.
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

    // And the subdirectories asked for by name, which is where what the *user* wrote lives -
    // agents, commands, plugins - as against what the tool wrote about them. The preset's list
    // is what every session of that software wants; a user preset attached to one project or
    // agent can carry any extra state that ground needs, without another override layer here.
    for from in &pr.seed {
        let from = expand(from);
        let Ok(rel) = Path::new(&from).strip_prefix(host) else {
            continue; // a seed for some other private path, or for another preset's
        };
        let to = copy.join(rel);
        // Said out loud, both ways. A seed path that is not there is *usually* honest - no two
        // machines keep all of what a preset names - but it is also exactly how a typo looks,
        // and `pi` shipped naming three directories it has never made without a word about it.
        // Twice the answer was "seeding worked, look elsewhere" when nothing had been copied.
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

/// Recursively copies an existing seed entry, omitting paths in `skip`; missing sources are
/// allowed because presets describe optional software state.
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
