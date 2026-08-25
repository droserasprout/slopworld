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
    pub clipboard: bool,
    pub desktop_open: bool,
    pub per_session_limits: bool,
    pub host_network_is_container: bool,
    pub host_terminals_are_container: bool,
}

pub fn is_slopcar() -> bool {
    std::env::var("SLOPD_RUNTIME").is_ok_and(|value| value == SLOPCAR)
}

pub fn capabilities() -> Capabilities {
    let sidecar = is_slopcar();
    Capabilities {
        runtime: if sidecar { SLOPCAR } else { "native" },
        audio_playback: !sidecar,
        clipboard: !sidecar,
        desktop_open: !sidecar,
        per_session_limits: !sidecar,
        host_network_is_container: sidecar,
        host_terminals_are_container: sidecar,
    }
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
    // port is chosen by `slopcar --port` (7717 by default) and carried into `endpoint.toml`.
    if bind.ip() != IpAddr::V4(Ipv4Addr::UNSPECIFIED) {
        bail!(
            "slopcar requires [daemon] bind = \"0.0.0.0:<port>\"; Docker publishes it only on Mac loopback"
        );
    }
    if cfg.daemon.token.trim().is_empty() {
        bail!("slopcar requires a non-empty [daemon] token");
    }
    for project in &cfg.projects {
        validate_slopcar_limits(&project.limits)?;
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
mod tests {
    use super::*;
    use crate::config::{Daemon, ProjectCfg};

    fn sidecar_config() -> Config {
        Config {
            daemon: Daemon {
                bind: "0.0.0.0:7717".into(),
                token: "secret".into(),
                ..Default::default()
            },
            ..Default::default()
        }
    }

    #[test]
    fn sidecar_requires_the_published_bind_and_a_token() {
        let mut cfg = sidecar_config();
        assert!(validate_slopcar_config(&cfg).is_ok());

        // A non-default port is fine as long as the bind stays the IPv4 wildcard: `slopcar --port`
        // publishes whatever port it seeds, so a sidecar can coexist beside a native daemon.
        cfg.daemon.bind = "0.0.0.0:7718".into();
        assert!(validate_slopcar_config(&cfg).is_ok());

        cfg.daemon.bind = "127.0.0.1:7717".into();
        assert!(validate_slopcar_config(&cfg)
            .unwrap_err()
            .to_string()
            .contains("0.0.0.0"));

        cfg.daemon.bind = "0.0.0.0:7717".into();
        cfg.daemon.token = "  ".into();
        assert!(validate_slopcar_config(&cfg)
            .unwrap_err()
            .to_string()
            .contains("non-empty"));
    }

    #[test]
    fn sidecar_rejects_inner_resource_limits() {
        let mut cfg = sidecar_config();
        cfg.projects.push(ProjectCfg {
            limits: Limits {
                memory_mb: Some(512),
                ..Default::default()
            },
            ..Default::default()
        });
        assert!(validate_slopcar_config(&cfg)
            .unwrap_err()
            .to_string()
            .contains("outer container budget"));
    }

    #[test]
    fn native_capabilities_keep_host_integrations() {
        let caps = Capabilities {
            runtime: "native",
            audio_playback: true,
            clipboard: true,
            desktop_open: true,
            per_session_limits: true,
            host_network_is_container: false,
            host_terminals_are_container: false,
        };
        assert!(caps.audio_playback && caps.per_session_limits);
    }
}
