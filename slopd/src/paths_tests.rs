use super::*;

#[test]
fn stores_follow_their_independent_application_roots() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    for (key, child) in [
        ("SLOPD_CONFIG_ROOT", "config"),
        ("SLOPD_DATA", "data"),
        ("SLOPD_CACHE", "cache"),
    ] {
        crate::test_support::set_env(key, root.join(child));
    }
    assert_eq!(config_root(), root.join("config"));
    assert_eq!(config_file(), root.join("config/config.toml"));
    assert_eq!(data_root(), root.join("data"));
    assert_eq!(cache_root(), root.join("cache"));
    assert_eq!(crate::sandbox::state_root(), root.join("data/sessions"));
    for child in ["config", "data"] {
        assert!(crate::sandbox::refused(root.join(child).to_str().unwrap()).is_some());
    }
}

#[cfg(unix)]
#[test]
fn normalized_bindings_follow_aliases_and_keep_missing_suffixes() {
    let root = std::env::temp_dir().join(format!("slopd-binding-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(root.join("real")).unwrap();
    std::os::unix::fs::symlink(root.join("real"), root.join("alias")).unwrap();
    assert_eq!(
        normalize(&root.join("alias/missing/record.toml")).unwrap(),
        root.join("real/missing/record.toml")
    );
    std::os::unix::fs::symlink(root.join("absent"), root.join("dangling")).unwrap();
    normalize(&root.join("dangling/record.toml")).unwrap_err();
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn sorted_directory_reads_preserve_order_and_caller_error_policy() {
    let root = std::env::temp_dir().join(format!("slopd-scan-{}", uuid::Uuid::new_v4()));
    assert!(read_sorted_dir(&root, Err).unwrap().is_empty());
    std::fs::create_dir(&root).unwrap();
    for name in ["z.toml", "a.TOML", "m.txt"] {
        std::fs::write(root.join(name), "").unwrap();
    }
    assert_eq!(
        read_sorted_dir(&root, Err).unwrap(),
        ["a.TOML", "m.txt", "z.toml"].map(|name| root.join(name))
    );
    let blocked = root.join("z.toml");
    read_sorted_dir(&blocked, Err).unwrap_err();
    let mut failures = 0;
    assert!(
        read_sorted_dir(&blocked, |_| {
            failures += 1;
            Ok(())
        })
        .unwrap()
        .is_empty()
    );
    assert_eq!(failures, 1);
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn root_uses_the_supplied_base_or_the_current_directory() {
    assert_eq!(
        root(Some(PathBuf::from("/tmp/config"))),
        PathBuf::from("/tmp/config/slopworld")
    );
    assert_eq!(root(None), PathBuf::from("./slopworld"));
}

#[test]
fn dir_uses_its_environment_override() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let variable = format!("SLOPD_PATH_TEST_{}", std::process::id());
    let selected = std::env::temp_dir().join("slopd-path-override");
    crate::test_support::set_env(&variable, &selected);

    assert_eq!(
        override_path(&variable, PathBuf::from("/ignored/sessions")),
        selected
    );
}

#[test]
fn dir_appends_the_child_below_the_application_root() {
    assert_eq!(
        override_path(
            "SLOPD_PATH_TEST_UNSET",
            root(Some(PathBuf::from("/var/lib"))).join("sessions")
        ),
        PathBuf::from("/var/lib/slopworld/sessions")
    );
}

#[test]
fn dir_stamp_returns_none_for_a_missing_directory() {
    let path = std::env::temp_dir().join(format!("slopworld-missing-dir-{}", std::process::id()));
    assert_eq!(dir_stamp(&path), None);
}

#[test]
fn private_toml_is_written_with_owner_only_permissions() {
    let root = std::env::temp_dir().join(format!(
        "slopworld-private-toml-{}-{}",
        std::process::id(),
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_nanos()
    ));
    let path = root.join("nested/cache.toml");

    write_private_toml(&path, "secret = true\n").unwrap();
    assert_eq!(std::fs::read_to_string(&path).unwrap(), "secret = true\n");
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        assert_eq!(
            std::fs::metadata(&path).unwrap().permissions().mode() & 0o777,
            0o600
        );
    }
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn atomic_replacement_cleans_a_temporary_file_when_install_fails() {
    let root = std::env::temp_dir().join(format!(
        "slopworld-atomic-failure-{}-{}",
        std::process::id(),
        uuid::Uuid::new_v4()
    ));
    std::fs::create_dir_all(&root).unwrap();
    let target = root.join("target.toml");
    std::fs::create_dir(&target).unwrap();

    assert!(write_atomic(&target, "new", None).is_err());
    let leftovers: Vec<_> = std::fs::read_dir(&root)
        .unwrap()
        .filter_map(|entry| entry.ok())
        .map(|entry| entry.path())
        .collect();
    assert_eq!(leftovers, vec![target]);
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn atomic_replacement_preserves_a_preexisting_temporary_file() {
    let root = std::env::temp_dir().join(format!("slopworld-temp-owner-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&root).unwrap();
    let target = root.join("target.toml");
    let temp = target.with_extension("toml.tmp");
    std::fs::write(&temp, "someone else's data").unwrap();
    write_atomic(&target, "secret", Some(0o600)).unwrap();
    assert_eq!(
        std::fs::read_to_string(&temp).unwrap(),
        "someone else's data"
    );
    assert_eq!(std::fs::read_to_string(&target).unwrap(), "secret");
    std::fs::remove_dir_all(root).unwrap();
}

#[tokio::test]
async fn async_atomic_replacement_keeps_private_store_permissions() {
    let root = std::env::temp_dir().join(format!(
        "slopworld-atomic-async-{}-{}",
        std::process::id(),
        uuid::Uuid::new_v4()
    ));
    let path = root.join("nested/config.toml");
    std::fs::create_dir_all(path.parent().unwrap()).unwrap();
    std::fs::write(&path, "old contents").unwrap();
    write_atomic_async(&path, "secret = true\n", Some(0o600))
        .await
        .unwrap();
    assert_eq!(std::fs::read_to_string(&path).unwrap(), "secret = true\n");
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        assert_eq!(
            std::fs::metadata(&path).unwrap().permissions().mode() & 0o777,
            0o600
        );
    }
    std::fs::remove_dir_all(root).unwrap();
}

#[cfg(target_os = "linux")]
#[tokio::test]
async fn asynchronous_write_reports_background_write_failure() {
    let mut file = tokio::fs::OpenOptions::new()
        .write(true)
        .open("/dev/full")
        .await
        .unwrap();
    assert!(
        write_and_finish(&mut file, b"must not be published")
            .await
            .is_err()
    );
}

#[tokio::test]
async fn async_replacement_ignores_leftovers_and_publishes_complete_contents() {
    let root = std::env::temp_dir().join(format!("slopworld-leftovers-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&root).unwrap();
    let target = root.join("config.toml");
    let leftover = temp_path(&target);
    std::fs::write(&leftover, "interrupted save").unwrap();
    let text = "complete contents".repeat(200_000);
    write_atomic_async(&target, &text, None).await.unwrap();
    assert_eq!(std::fs::read_to_string(&target).unwrap(), text);
    assert_eq!(
        std::fs::read_to_string(&leftover).unwrap(),
        "interrupted save"
    );
    std::fs::remove_dir_all(root).unwrap();
}
