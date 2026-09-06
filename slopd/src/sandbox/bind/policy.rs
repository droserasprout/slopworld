//! Host-path policy used while assembling sandbox mounts.
//!
//! Every candidate path still passes through the central `refused` guard. This module only
//! resolves effective preset paths and chooses existing host-side sources; it does not emit a
//! bwrap argument or widen the set of paths that the guard permits.

use std::path::{Path, PathBuf};

use crate::config::{expand, Config, ProjectCfg, SessionCfg};
use crate::presets::Table;

use crate::sandbox::{presets_for, private_path, refused};

pub(super) fn private_bind_paths(
    cfg: &Config,
    s: &SessionCfg,
    p: &ProjectCfg,
    t: &Table,
) -> Vec<(String, String)> {
    let mut out: Vec<(String, String)> = Vec::new();
    for pr in presets_for(cfg, s, p, t) {
        for path in &pr.private {
            let host = expand(path);
            if host.is_empty() {
                continue;
            }
            let copy = private_path(&s.state_id, &host);
            // The host path or the prepared session-state copy must exist. Paths under /tmp/
            // never exist on the host (the skeleton mounts its own tmpfs there), but
            // prepare_network creates the copy beforehand.
            if !Path::new(&host).exists() && !copy.exists() {
                continue;
            }
            if out.iter().any(|(_, seen)| seen == &host) {
                continue;
            }
            out.push((copy.to_string_lossy().into_owned(), host));
        }
    }
    out
}

/// Returns existing regular files that presets may share read-write into private state.
/// Refused paths and directories are excluded so shared state cannot grant execution or reach
/// another session's secrets.
pub(super) fn shared_binds(cfg: &Config, s: &SessionCfg, p: &ProjectCfg, t: &Table) -> Vec<String> {
    let mut out: Vec<String> = Vec::new();
    for pr in presets_for(cfg, s, p, t) {
        for path in &pr.shared {
            let host = expand(path);
            if host.is_empty() || out.contains(&host) {
                continue;
            }
            if let Some(what) = refused(&host) {
                tracing::warn!("not sharing {host}: it reaches {what}");
                continue;
            }
            if !Path::new(&host).exists() {
                continue;
            }
            if !Path::new(&host).is_file() {
                tracing::warn!("not sharing {host}: shared paths are files, and this is not one");
                continue;
            }
            out.push(host);
        }
    }
    out
}

/// The tmux server is outside bwrap and uses the host uid in its socket directory. A bwrap
/// session is uid 0 in its user namespace, so a debug preset gets the host directory mounted at
/// the path tmux will calculate inside the session. The bind is read-only: clients talk through
/// the socket, while an absent or stale server costs the preset nothing.
pub(super) fn tmux_socket_bind(socket: &str) -> Option<(String, String)> {
    if socket.trim().is_empty() {
        return None;
    }
    let root = std::env::var_os("TMUX_TMPDIR")
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from("/tmp"));
    let host_dir = root.join(format!("tmux-{}", nix::unistd::getuid().as_raw()));
    if !host_dir.is_dir() || !host_dir.join(socket).exists() {
        return None;
    }
    let host_dir = host_dir.to_string_lossy().into_owned();
    if let Some(what) = refused(&host_dir) {
        tracing::warn!("not exposing tmux socket directory {host_dir}: it reaches {what}");
        return None;
    }
    Some((host_dir, "/tmp/tmux-0".into()))
}

/// The debug capability names these files structurally instead of putting their parent in an
/// ordinary path list. That keeps presets and future config-directory contents out of reach.
pub(super) fn daemon_config_binds() -> Vec<String> {
    [Config::path_in_use(), crate::endpoint::path()]
        .into_iter()
        .filter(|path| path.is_file())
        .map(|path| path.to_string_lossy().into_owned())
        .collect()
}

/// `/etc/resolv.conf` is commonly a symlink into `/run`, which is not mounted in the sandbox.
/// Bind the replacement onto the real target so the symlink still resolves inside bwrap.
pub(super) fn resolver_target() -> Option<String> {
    std::fs::canonicalize("/etc/resolv.conf")
        .ok()
        .map(|p| p.to_string_lossy().into_owned())
        .filter(|target| target != "/etc/resolv.conf")
}

/// Expanded, dropped if they are not on this host, and deduplicated. The guard is deliberately
/// kept here at the final resolution boundary as well as in preset validation: config and
/// preset data can outlive the binary that first accepted them.
pub(super) fn paths(
    presets: &[&crate::presets::SandboxPreset],
    pick: fn(&crate::presets::SandboxPreset) -> &[String],
) -> Vec<String> {
    let from_presets: Vec<String> = presets
        .iter()
        .flat_map(|pr| pick(pr).iter().cloned())
        .collect();

    let mut out: Vec<String> = Vec::new();
    for path in from_presets {
        let path = expand(&path);
        if path.is_empty() || out.contains(&path) {
            continue;
        }
        // Warned about and dropped rather than refused, the way an unknown preset name is:
        // the files outlive the binary, and a line somebody wrote a year ago is not grounds
        // for an agent that will not start. It is still never bound.
        if let Some(what) = refused(&path) {
            tracing::warn!("not binding {path}: it reaches {what}");
            continue;
        }
        if Path::new(&path).exists() {
            out.push(path);
        }
    }
    out
}
