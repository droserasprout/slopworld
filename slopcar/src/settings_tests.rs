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
fn standalone_defaults_match_recipe_state_directories_and_use_xdg_overrides() {
    let default = settings(&[("HOME", "/home/test")]).unwrap();
    assert_eq!(
        default.config,
        PathBuf::from("/home/test/.config/slopworld-car")
    );
    assert_eq!(
        default.data,
        PathBuf::from("/home/test/.local/share/slopworld-car")
    );
    assert_eq!(default.port, 7718);
    let custom = settings(&[
        ("HOME", "/home/test"),
        ("XDG_CONFIG_HOME", "/config"),
        ("XDG_DATA_HOME", "/data"),
    ])
    .unwrap();
    assert_eq!(custom.config, PathBuf::from("/config/slopworld-car"));
    assert_eq!(custom.data, PathBuf::from("/data/slopworld-car"));
}

#[test]
fn explicit_state_overrides_preserve_existing_installations_and_invalid_ports_fail() {
    let custom = settings(&[
        ("HOME", "/home/test"),
        ("XDG_CONFIG_HOME", "/config"),
        ("SLOPCAR_CONFIG_DIR", "/old config/slopworld"),
        ("SLOPCAR_DATA_DIR", "/old data/slopworld"),
    ])
    .unwrap();
    assert_eq!(custom.config, PathBuf::from("/old config/slopworld"));
    assert_eq!(custom.data, PathBuf::from("/old data/slopworld"));
    assert!(settings(&[("HOME", "/home/test"), ("SLOPCAR_PORT", "0")]).is_err());
}
