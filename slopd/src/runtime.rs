//! Runtime capability discovery and sidecar configuration validation.

use std::net::{IpAddr, Ipv4Addr, SocketAddr};

use anyhow::{Context, Result, bail};
use serde::Serialize;

use crate::config::{Config, Limits};

pub const SLOPCAR: &str = "slopcar";

/// Runtime differences that affect native client controls. The default is the Linux host daemon.
/// Only the sidecar entrypoint sets `SLOPD_RUNTIME=slopcar`.
#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
pub struct Capabilities {
    pub runtime: &'static str,
    pub audio_playback: bool,
    pub ncspot: bool,
    pub clipboard: bool,
    pub desktop_open: bool,
    pub per_session_limits: bool,
    pub host_network_is_container: bool,
    pub host_terminals_are_container: bool,
    pub terminal: TerminalCapabilities,
}

/// Limits shared by the daemon and clients.
/// The daemon uses these values to limit allocations and scroll requests.
/// Clients use them to limit caches and UI calculations.
#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
pub struct TerminalCapabilities {
    pub scrollback_lines: u32,
    pub min_cols: u16,
    pub max_cols: u16,
    pub min_rows: u16,
    pub max_rows: u16,
}

pub fn is_slopcar() -> bool {
    std::env::var("SLOPD_RUNTIME").is_ok_and(|value| value == SLOPCAR)
}

pub fn capabilities() -> Capabilities {
    let sidecar = is_slopcar();
    Capabilities {
        runtime: if sidecar { SLOPCAR } else { "native" },
        audio_playback: !sidecar,
        ncspot: ncspot_available(),
        clipboard: !sidecar,
        desktop_open: !sidecar,
        per_session_limits: !sidecar,
        host_network_is_container: sidecar,
        host_terminals_are_container: sidecar,
        terminal: TerminalCapabilities {
            scrollback_lines: crate::tmux::SCROLLBACK_LINES,
            min_cols: crate::shared::protocol::TERMINAL_MIN_COLS,
            max_cols: crate::shared::protocol::TERMINAL_MAX_COLS,
            min_rows: crate::shared::protocol::TERMINAL_MIN_ROWS,
            max_rows: crate::shared::protocol::TERMINAL_MAX_ROWS,
        },
    }
}

/// Check whether the daemon can start the optional Spotify player.
/// Settings and the jukebox use this capability to show availability before the user selects the player.
pub fn ncspot_available() -> bool {
    if !cfg!(target_os = "linux") || is_slopcar() {
        return false;
    }

    let Some(path) = std::env::var_os("PATH") else {
        return false;
    };
    std::env::split_paths(&path).any(|dir| executable(dir.join("ncspot")))
}

/// Find executables through the daemon's PATH.
/// Settings uses this snapshot for native and sidecar deployments.
/// An empty path means the daemon cannot find the executable in its environment.
pub fn whereis() -> Vec<BinaryLocation> {
    const NAMES: &[&str] = &[
        "tmux",
        "bwrap",
        "pasta",
        "systemd-run",
        "ps",
        "rg",
        "git",
        "env",
        "bash",
        "zsh",
        "fish",
        "nu",
        "pwsh",
        "sh",
        "claude",
        "codex",
        "opencode",
        "pi",
        "less",
        "delta",
        "more",
        "bat",
        "highlight",
        "micro",
        "vim",
        "nvim",
        "nano",
        "emacsclient",
        "gio",
        "gdbus",
        "wl-copy",
        "wl-paste",
        "xclip",
        "xsel",
        "ncspot",
    ];

    NAMES
        .iter()
        .map(|name| BinaryLocation {
            name: (*name).into(),
            path: find_executable(name).unwrap_or_default(),
        })
        .collect()
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize)]
pub struct BinaryLocation {
    pub name: String,
    pub path: String,
}

fn find_executable(name: &str) -> Option<String> {
    let path = std::env::var_os("PATH")?;
    std::env::split_paths(&path)
        .map(|dir| dir.join(name))
        .find(|path| executable(path.clone()))
        .map(|path| path.to_string_lossy().into_owned())
}

fn executable(path: std::path::PathBuf) -> bool {
    let Ok(metadata) = std::fs::metadata(path) else {
        return false;
    };
    if !metadata.is_file() {
        return false;
    }
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        metadata.permissions().mode() & 0o111 != 0
    }
    #[cfg(not(unix))]
    {
        true
    }
}

pub fn hostname() -> String {
    nix::sys::utsname::uname()
        .map(|name| name.nodename().to_string_lossy().into_owned())
        .unwrap_or_else(|_| "unknown".into())
}

pub fn validate_runtime_name() -> Result<()> {
    match std::env::var("SLOPD_RUNTIME") {
        Ok(value) if value != SLOPCAR => {
            bail!("Unknown SLOPD_RUNTIME value {value:?}. Expected {SLOPCAR:?}.")
        }
        Err(std::env::VarError::NotUnicode(_)) => bail!("SLOPD_RUNTIME must be valid UTF-8"),
        Ok(_) | Err(std::env::VarError::NotPresent) => Ok(()),
    }
}

pub fn validate_config(cfg: &Config) -> Result<()> {
    if !is_slopcar() {
        return Ok(());
    }
    validate_slopcar_config(cfg)
}

fn validate_slopcar_config(cfg: &Config) -> Result<()> {
    let bind = cfg
        .daemon
        .bind
        .parse::<SocketAddr>()
        .with_context(|| format!("bad sidecar bind address {:?}", cfg.daemon.bind))?;
    // Bind the IPv4 wildcard so Docker can publish the port on the Mac loopback interface.
    // `slopcar --port` selects the port, with 7718 as the default. Store that port in endpoint.toml.
    if bind.ip() != IpAddr::V4(Ipv4Addr::UNSPECIFIED) {
        bail!(
            "slopcar requires `[daemon] bind = \"0.0.0.0:<port>\"`. Docker publishes this port on Mac loopback only."
        );
    }
    if cfg.daemon.token.trim().is_empty() {
        bail!("slopcar requires a non-empty [daemon] token");
    }
    for session in &cfg.sessions {
        validate_slopcar_limits(&session.limits)
            .with_context(|| format!("session {:?}", session.name))?;
    }
    Ok(())
}

pub fn validate_limits(limits: &Limits) -> Result<()> {
    if is_slopcar() {
        validate_slopcar_limits(limits)?;
    }
    Ok(())
}

fn validate_slopcar_limits(limits: &Limits) -> Result<()> {
    if !limits.is_empty() {
        bail!(
            "Per-agent resource limits are unavailable in slopcar. Set the outer container budget instead."
        );
    }
    Ok(())
}

#[cfg(test)]
#[path = "runtime_tests.rs"]
mod tests;
