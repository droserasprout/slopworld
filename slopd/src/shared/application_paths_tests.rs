use super::*;

#[test]
fn home_relative_roots_and_explicit_paths_expand_before_consumption() {
    let lookup = |key: &str| match key {
        "HOME" => Some(OsString::from("/home/test")),
        "SLOPD_CONFIG_ROOT" => Some(OsString::from("~/config")),
        "SLOPD_DATA" => Some(OsString::from("~/data")),
        "SLOPD_CACHE" => Some(OsString::from("~/cache")),
        _ => None,
    };
    assert_eq!(
        Root::Config.resolve_with(lookup),
        PathBuf::from("/home/test/config")
    );
    assert_eq!(
        Root::Data.resolve_with(lookup),
        PathBuf::from("/home/test/data")
    );
    assert_eq!(
        Root::Cache.resolve_with(lookup),
        PathBuf::from("/home/test/cache")
    );
    for mode in [Mode::Native, Mode::Sidecar] {
        let config = mode.config_dir_with(lookup);
        assert_eq!(
            mode.endpoint_path_with(lookup),
            config.join("endpoints/local.toml")
        );
        let explicit = |key| match key {
            "SLOPD_ENDPOINT" => Some(OsString::from("~/endpoint.toml")),
            "SLOPWORLD_PROFILE" => Some(OsString::from("~/profile")),
            "SLOPWORLD_GAME_LOG" => Some(OsString::from("~/Player.log")),
            _ => lookup(key),
        };
        assert_eq!(
            mode.endpoint_path_with(explicit),
            PathBuf::from("/home/test/endpoint.toml")
        );
        assert_eq!(
            mode.profile_path_with(explicit),
            PathBuf::from("/home/test/profile")
        );
        assert_eq!(
            mode.game_log_path_with(explicit),
            PathBuf::from("/home/test/Player.log")
        );
    }
    assert_eq!(
        expand_home(PathBuf::from("relative/path"), lookup),
        PathBuf::from("relative/path")
    );
    assert_eq!(
        expand_home(PathBuf::from("~someone/path"), lookup),
        PathBuf::from("~someone/path")
    );
}

#[test]
fn roots_are_independent_and_endpoint_override_wins() {
    let lookup = |key: &str| match key {
        "SLOPD_CONFIG_ROOT" => Some(OsString::from("/chosen/config")),
        "SLOPD_DATA" => Some(OsString::from("/chosen/data")),
        "SLOPD_CACHE" => Some(OsString::from("/chosen/cache")),
        _ => None,
    };
    assert_eq!(
        Root::Config.resolve_with(lookup),
        PathBuf::from("/chosen/config")
    );
    assert_eq!(
        Root::Data.resolve_with(lookup),
        PathBuf::from("/chosen/data")
    );
    assert_eq!(
        Root::Cache.resolve_with(lookup),
        PathBuf::from("/chosen/cache")
    );
    assert_eq!(
        Mode::Native.endpoint_path_with(lookup),
        PathBuf::from("/chosen/config/endpoints/local.toml")
    );
    assert_eq!(
        Mode::Native.game_log_path_with(lookup),
        PathBuf::from("/chosen/cache/Player.log")
    );
    assert_eq!(
        Mode::Native.endpoint_path_with(|key| {
            if key == "SLOPD_ENDPOINT" {
                Some(OsString::from("/explicit/endpoint.toml"))
            } else {
                lookup(key)
            }
        }),
        PathBuf::from("/explicit/endpoint.toml")
    );
}

#[test]
fn game_log_override_wins_and_blank_values_use_the_cache_root() {
    for value in ["/explicit/game.log", "", "   "] {
        let path = Mode::Native.game_log_path_with(|key| match key {
            "SLOPWORLD_GAME_LOG" => Some(OsString::from(value)),
            "SLOPD_CACHE" => Some(OsString::from("/cache")),
            _ => None,
        });
        let expected = if value.trim().is_empty() {
            "/cache/Player.log"
        } else {
            value
        };
        assert_eq!(path, PathBuf::from(expected));
    }
}

#[test]
fn sidecar_paths_use_separate_subdirectories_and_common_overrides() {
    let lookup = |key: &str| match key {
        "SLOPD_CONFIG_ROOT" => Some(OsString::from("/config")),
        "SLOPD_DATA" => Some(OsString::from("/data")),
        "SLOPD_CACHE" => Some(OsString::from("/cache")),
        _ => None,
    };
    assert_eq!(
        Mode::Sidecar.config_dir_with(lookup),
        PathBuf::from("/config/sidecar")
    );
    assert_eq!(
        Mode::Sidecar.daemon_data_dir_with(lookup),
        PathBuf::from("/data/sidecar/daemon")
    );
    assert_eq!(
        Mode::Sidecar.endpoint_path_with(lookup),
        PathBuf::from("/config/sidecar/endpoints/local.toml")
    );
    assert_eq!(
        Mode::Sidecar.profile_path_with(lookup),
        PathBuf::from("/data/sidecar/profiles/local")
    );
    assert_eq!(
        Mode::Sidecar.game_log_path_with(lookup),
        PathBuf::from("/cache/sidecar/Player.log")
    );
    let explicit = |key: &str| match key {
        "SLOPD_ENDPOINT" => Some(OsString::from("/explicit/endpoint.toml")),
        "SLOPWORLD_PROFILE" => Some(OsString::from("/explicit/profile")),
        "SLOPWORLD_GAME_LOG" => Some(OsString::from("/explicit/Player.log")),
        _ => lookup(key),
    };
    for mode in [Mode::Native, Mode::Sidecar] {
        assert_eq!(
            mode.endpoint_path_with(explicit),
            PathBuf::from("/explicit/endpoint.toml")
        );
        assert_eq!(
            mode.profile_path_with(explicit),
            PathBuf::from("/explicit/profile")
        );
        assert_eq!(
            mode.game_log_path_with(explicit),
            PathBuf::from("/explicit/Player.log")
        );
    }
}

#[test]
fn profiles_follow_connection_names_and_explicit_profiles_win() {
    for (mode, base) in [(Mode::Native, "/data"), (Mode::Sidecar, "/data/sidecar")] {
        for name in ["local", "tank", "another-host"] {
            let endpoint = format!("/config/endpoints/{name}.toml");
            let lookup = |key| match key {
                "SLOPD_DATA" => Some(OsString::from("/data")),
                "SLOPD_ENDPOINT" => Some(OsString::from(&endpoint)),
                _ => None,
            };
            assert_eq!(
                mode.profile_path_with(lookup),
                PathBuf::from(base).join("profiles").join(name)
            );
            assert_eq!(
                mode.profile_path_with(|key| match key {
                    "HOME" => Some(OsString::from("/home/test")),
                    "SLOPWORLD_PROFILE" => Some(OsString::from("~/chosen-profile")),
                    _ => lookup(key),
                }),
                PathBuf::from("/home/test/chosen-profile")
            );
        }
    }
}
