use std::net::{IpAddr, Ipv4Addr, SocketAddr};

use anyhow::{bail, Context, Result};
use serde::Serialize;

use crate::config::{Config, Limits};

pub const SLOPCAR: &str = "slopcar";

/// Runtime differences that affect controls in the native client. The default is the existing
/// Linux host daemon; `SLOPD_RUNTIME=slopcar` is set only by the sidecar entrypoint.
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

/// Limits that are observable at the daemon/client boundary. The daemon clamps allocations and
/// scroll requests with these values; clients use them only to bound their own caches and UI
/// calculations.
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
            scrollback_lines: crate::config::SCROLLBACK_LINES,
            min_cols: crate::shared::protocol::TERMINAL_MIN_COLS,
            max_cols: crate::shared::protocol::TERMINAL_MAX_COLS,
            min_rows: crate::shared::protocol::TERMINAL_MIN_ROWS,
            max_rows: crate::shared::protocol::TERMINAL_MAX_ROWS,
        },
    }
}

/// Whether the daemon can start the optional Spotify player. Keep this a capability rather
/// than waiting for the first launch attempt: Settings and the jukebox need to make the
/// unavailable source legible before a user clicks it.
pub fn ncspot_available() -> bool {
    if !cfg!(target_os = "linux") || is_slopcar() {
        return false;
    }

    let Some(path) = std::env::var_os("PATH") else {
        return false;
    };
    std::env::split_paths(&path).any(|dir| executable(dir.join("ncspot")))
}

/// Executables whose effective PATH belongs to the daemon rather than to the game process.
/// Settings uses this snapshot for native and sidecar deployments alike; an empty path means
/// the daemon cannot resolve the name in its own environment.
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
            bail!("unknown SLOPD_RUNTIME {value:?}; expected {SLOPCAR:?}")
        }
        _ => Ok(()),
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
    // The daemon must bind the IPv4 wildcard so Docker can publish it on the Mac's loopback; the
    // port is chosen by `slopcar --port` (7718 by default) and carried into `endpoint.toml`.
    if bind.ip() != IpAddr::V4(Ipv4Addr::UNSPECIFIED) {
        bail!(
            "slopcar requires [daemon] bind = \"0.0.0.0:<port>\"; Docker publishes it only on Mac loopback"
        );
    }
    if cfg.daemon.token.trim().is_empty() {
        bail!("slopcar requires a non-empty [daemon] token");
    }
    for session in &cfg.sessions {
        validate_slopcar_limits(&session.limits)?;
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
            "per-agent resource limits are unavailable in slopcar; set the outer container budget instead"
        );
    }
    Ok(())
}

#[cfg(test)]
#[path = "runtime_tests.rs"]
mod tests;
