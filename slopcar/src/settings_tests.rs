#![allow(clippy::unwrap_used, reason = "Test assertions fail immediately")]
use super::*;

fn settings(values: &[(&str, &str)]) -> Result<Settings> {
    Settings::from_env(|key| {
        values
            .iter()
            .find(|(name, _)| *name == key)
            .map(|(_, value)| OsString::from(value))
    })
}

#[test]
fn standalone_defaults_use_shared_application_roots() {
    let default = settings(&[("HOME", "/home/test")]).unwrap();
    let base = if cfg!(target_os = "macos") {
        "/home/test/Library/Application Support/slopworld"
    } else {
        "/home/test/.config/slopworld"
    };
    assert_eq!(default.config, PathBuf::from(base).join("sidecar"));
    let base = if cfg!(target_os = "macos") {
        "/home/test/Library/Application Support/slopworld"
    } else {
        "/home/test/.local/share/slopworld"
    };
    assert_eq!(default.data, PathBuf::from(base).join("sidecar/daemon"));
    assert_eq!(default.port, 7718);
}

#[test]
fn shared_root_overrides_win_over_xdg_and_invalid_ports_fail() {
    let custom = settings(&[
        ("HOME", "/home/test"),
        ("XDG_CONFIG_HOME", "/ignored"),
        ("SLOPD_CONFIG_ROOT", "/chosen config"),
        ("SLOPD_DATA", "/chosen data"),
    ])
    .unwrap();
    assert_eq!(custom.config, PathBuf::from("/chosen config/sidecar"));
    assert_eq!(custom.data, PathBuf::from("/chosen data/sidecar/daemon"));
    assert!(settings(&[("HOME", "/home/test"), ("SLOPCAR_PORT", "0")]).is_err());
}
