use super::*;
use crate::config::{Daemon, Limits, SessionCfg};

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
    validate_slopcar_config(&cfg).unwrap();

    // A non-default port is fine as long as the bind stays the IPv4 wildcard. `slopcar --port`
    // publishes whatever port it seeds, so a sidecar can coexist beside a native daemon.
    cfg.daemon.bind = "0.0.0.0:7718".into();
    validate_slopcar_config(&cfg).unwrap();

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
    cfg.sessions.push(SessionCfg {
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
        ncspot: true,
        clipboard: true,
        desktop_open: true,
        per_session_limits: true,
        host_network_is_container: false,
        host_terminals_are_container: false,
        terminal: TerminalCapabilities {
            scrollback_lines: crate::tmux::SCROLLBACK_LINES,
            min_cols: crate::shared::protocol::TERMINAL_MIN_COLS,
            max_cols: crate::shared::protocol::TERMINAL_MAX_COLS,
            min_rows: crate::shared::protocol::TERMINAL_MIN_ROWS,
            max_rows: crate::shared::protocol::TERMINAL_MAX_ROWS,
        },
    };
    assert!(caps.audio_playback && caps.per_session_limits);
}
