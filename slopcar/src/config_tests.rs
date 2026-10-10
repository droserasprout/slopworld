#![allow(
    clippy::unwrap_used,
    reason = "Test setup and assertions fail immediately"
)]
use super::*;
#[test]
fn repeated_seed_preserves_token_and_rejects_changed_port() {
    let root = tempfile::tempdir().unwrap();
    let config = root.path().join("config");
    let data = root.path().join("data");
    seed(&config, &data, 7718).unwrap();
    let original = fs::read(config.join("config.toml")).unwrap();
    seed(&config, &data, 7718).unwrap();
    assert_eq!(fs::read(config.join("config.toml")).unwrap(), original);
    assert!(seed(&config, &data, 7719).is_err());
    let parsed: toml::Value = toml::from_str(std::str::from_utf8(&original).unwrap()).unwrap();
    assert_eq!(parsed["daemon"]["token"].as_str().unwrap().len(), 64);
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        assert_eq!(
            fs::metadata(config.join("config.toml"))
                .unwrap()
                .permissions()
                .mode()
                & 0o777,
            0o600
        );
        assert_eq!(
            fs::metadata(&config).unwrap().permissions().mode() & 0o777,
            0o700
        );
    }
}
#[test]
fn malformed_config_is_preserved_and_ipv6_port_is_validated() {
    let root = tempfile::tempdir().unwrap();
    let config = root.path().join("config");
    let data = root.path().join("data");
    fs::create_dir(&config).unwrap();
    let path = config.join("config.toml");
    fs::write(&path, "not valid toml").unwrap();
    assert!(seed(&config, &data, 7718).is_err());
    assert_eq!(fs::read_to_string(&path).unwrap(), "not valid toml");
    fs::write(&path, "[daemon]\nbind = '[::]:7718'\n").unwrap();
    seed(&config, &data, 7718).unwrap();
    assert!(seed(&config, &data, 7719).is_err());
}
