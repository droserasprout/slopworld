//! Help, version, and syntax errors exit before configuration or service startup.
use std::process::Command;

#[test]
fn local_cli_exits_without_loading_configuration_or_creating_state() {
    let root = std::env::temp_dir().join(format!("slopd-cli-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir(&root).expect("create CLI fixture");
    let config = root.join("config");
    // A regular file cannot serve as the config directory: reaching startup fails.
    std::fs::write(&config, "configuration must not be accessed").expect("write config blocker");
    let data = root.join("data");
    let cache = root.join("cache");
    for (args, code, expected) in [
        (vec!["--help"], 0, "Usage: slopd".to_owned()),
        (
            vec!["--version"],
            0,
            format!("slopd {}", env!("SLOPWORLD_VERSION")),
        ),
        (vec!["--perf-bench", "--help"], 0, "Usage: slopd".to_owned()),
        (vec!["--unknown"], 2, "unexpected argument".to_owned()),
        (
            vec!["--perf-bench", "extra"],
            2,
            "unexpected argument".to_owned(),
        ),
    ] {
        let output = Command::new(env!("CARGO_BIN_EXE_slopd"))
            .args(&args)
            .env("SLOPD_CONFIG_ROOT", &config)
            .env("SLOPD_DATA", &data)
            .env("SLOPD_CACHE", &cache)
            .env("PATH", &root)
            .output()
            .expect("run daemon CLI");
        assert_eq!(output.status.code(), Some(code), "{args:?}: {output:?}");
        let (text, other) = if code == 0 {
            (&output.stdout, &output.stderr)
        } else {
            (&output.stderr, &output.stdout)
        };
        assert!(
            String::from_utf8_lossy(text).contains(&expected),
            "{args:?}: {output:?}"
        );
        assert!(other.is_empty(), "{args:?}: {output:?}");
        assert!(!data.exists());
        assert!(!cache.exists());
    }
    assert_eq!(
        std::fs::read_to_string(config).expect("read config blocker"),
        "configuration must not be accessed"
    );
    std::fs::remove_dir_all(root).expect("remove CLI fixture");
}
