use super::*;
use crate::config::{Daemon, Limits, SessionCfg};

fn sidecar_config() -> Config {
    Config {
        settings: crate::config::Settings {
            daemon: Daemon {
                bind: "0.0.0.0:7717".into(),
                token: "secret".into(),
                ..Default::default()
            },
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
    assert!(
        validate_slopcar_config(&cfg)
            .unwrap_err()
            .to_string()
            .contains("0.0.0.0")
    );

    cfg.daemon.bind = "0.0.0.0:7717".into();
    cfg.daemon.token = "  ".into();
    assert!(
        validate_slopcar_config(&cfg)
            .unwrap_err()
            .to_string()
            .contains("non-empty")
    );
}

#[test]
fn sidecar_rejects_inner_resource_limits() {
    let mut cfg = sidecar_config();
    cfg.sessions.push(SessionCfg {
        name: "limited-session".into(),
        limits: Limits {
            memory_mb: Some(512),
            ..Default::default()
        },
        ..Default::default()
    });
    let error = validate_slopcar_config(&cfg).unwrap_err();
    assert!(format!("{error:#}").contains("outer container budget"));
    assert!(error.to_string().contains("limited-session"));
}

#[test]
fn native_capabilities_keep_host_integrations() {
    let Some(_) = crate::test_support::isolated_with_env(|command, _| {
        command.env_remove("SLOPD_RUNTIME");
    }) else {
        return;
    };
    let caps = capabilities();
    assert_eq!(caps.runtime, "native");
    assert!(caps.audio_playback && caps.clipboard && caps.desktop_open && caps.per_session_limits);
    assert!(!caps.host_network_is_container && !caps.host_terminals_are_container);
    assert_eq!(caps.ncspot, ncspot_available());
}

#[cfg(unix)]
#[test]
fn non_unicode_runtime_is_rejected() {
    use std::os::unix::ffi::OsStringExt;
    let Some(_) = crate::test_support::isolated_with_env(|command, _| {
        command.env("SLOPD_RUNTIME", std::ffi::OsString::from_vec(vec![0xff]));
    }) else {
        return;
    };
    assert!(
        validate_runtime_name()
            .unwrap_err()
            .to_string()
            .contains("UTF-8")
    );
}
