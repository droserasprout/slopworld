use super::*;

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
    let variable = format!("SLOPD_PATH_TEST_{}", std::process::id());
    let override_path = std::env::temp_dir().join("slopd-path-override");
    std::env::set_var(&variable, &override_path);

    assert_eq!(
        dir(&variable, Some(PathBuf::from("/ignored")), "sessions"),
        override_path
    );

    std::env::remove_var(variable);
}

#[test]
fn dir_appends_the_child_below_the_application_root() {
    assert_eq!(
        dir(
            "SLOPD_PATH_TEST_UNSET",
            Some(PathBuf::from("/var/lib")),
            "sessions"
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
    assert!(write_and_finish(&mut file, b"must not be published")
        .await
        .is_err());
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
