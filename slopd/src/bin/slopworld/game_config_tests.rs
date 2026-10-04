use super::*;

fn game(root: &Path, name: &str) -> PathBuf {
    let path = root.join(name);
    fs::create_dir_all(&path).unwrap();
    fs::write(path.join(crate::EXE), "fixture").unwrap();
    path
}

#[test]
fn saved_path_round_trips_and_successful_reinstall_replaces_it() {
    let root = crate::tests::scratch("game-config-roundtrip");
    let first = game(&root, "game with spaces and \"quotes\"");
    let second = game(&root, "second");
    let config = root.join("config/game.toml");
    save(&first, &config).unwrap();
    assert_eq!(
        resolve(None, None, || Ok(config.clone()), &[]).unwrap(),
        first
    );
    save(&second, &config).unwrap();
    assert_eq!(
        resolve(None, None, || Ok(config.clone()), &[]).unwrap(),
        second
    );
    assert_eq!(fs::read_dir(config.parent().unwrap()).unwrap().count(), 1);
    fs::remove_dir_all(root).unwrap();
}

#[test]
fn overrides_bypass_invalid_config_and_explicit_wins() {
    let root = crate::tests::scratch("game-config-precedence");
    let explicit = game(&root, "explicit");
    let environment = game(&root, "environment");
    let config = root.join("game.toml");
    fs::write(&config, "invalid toml").unwrap();
    assert_eq!(
        resolve(
            explicit.to_str(),
            environment.to_str(),
            || Ok(config.clone()),
            &[]
        )
        .unwrap(),
        explicit
    );
    assert_eq!(
        resolve(None, environment.to_str(), || Ok(config.clone()), &[]).unwrap(),
        environment
    );
    resolve(
        Some("/missing-game"),
        environment.to_str(),
        || Ok(config.clone()),
        &[],
    )
    .unwrap_err();
    fs::remove_dir_all(root).unwrap();
}

#[test]
fn only_missing_config_falls_back_and_stale_path_can_be_repaired() {
    let root = crate::tests::scratch("game-config-fallback");
    let fallback = game(&root, "fallback");
    let config = root.join("game.toml");
    let guesses = [fallback.clone()];
    assert_eq!(
        resolve(None, None, || Ok(config.clone()), &guesses).unwrap(),
        fallback
    );
    for text in ["invalid toml", "path = ''", "path = '/missing-game'"] {
        fs::write(&config, text).unwrap();
        let error = resolve(None, None, || Ok(config.clone()), &guesses).unwrap_err();
        assert!(error.contains("game.toml"), "{error}");
    }
    save(&fallback, &config).unwrap();
    assert_eq!(
        resolve(None, None, || Ok(config.clone()), &guesses).unwrap(),
        fallback
    );
    fs::remove_dir_all(root).unwrap();
}

#[test]
fn failed_save_preserves_previous_default() {
    let root = crate::tests::scratch("game-config-failed-save");
    let installed = game(&root, "installed");
    let config = root.join("game.toml");
    save(&installed, &config).unwrap();
    let before = fs::read(&config).unwrap();
    for directory in [&root, Path::new("")] {
        save(directory, &config).unwrap_err();
        assert_eq!(fs::read(&config).unwrap(), before);
    }
    assert_eq!(
        resolve(None, None, || Ok(config.clone()), &[]).unwrap(),
        installed
    );
    fs::remove_dir_all(root).unwrap();
}

#[test]
fn overrides_do_not_require_config_discovery() {
    let root = crate::tests::scratch("game-config-no-home");
    let installed = game(&root, "installed");
    for (explicit, environment) in [(installed.to_str(), None), (None, installed.to_str())] {
        assert_eq!(
            resolve(
                explicit,
                environment,
                || panic!("override must bypass config discovery"),
                &[]
            )
            .unwrap(),
            installed
        );
    }
    fs::remove_dir_all(root).unwrap();
}

#[cfg(unix)]
#[test]
fn dangling_config_symlink_blocks_fallback_and_save_repairs_it() {
    let root = crate::tests::scratch("game-config-dangling-link");
    let fallback = game(&root, "fallback");
    let config = root.join("game.toml");
    let missing = root.join("missing.toml");
    std::os::unix::fs::symlink(&missing, &config).unwrap();
    let guesses = [fallback.clone()];
    let error = resolve(None, None, || Ok(config.clone()), &guesses).unwrap_err();
    assert!(
        error.contains("reading") && error.contains("game.toml"),
        "{error}"
    );
    save(&fallback, &config).unwrap();
    assert_eq!(
        resolve(None, None, || Ok(config.clone()), &guesses).unwrap(),
        fallback
    );
    assert!(!missing.exists());
    fs::remove_dir_all(root).unwrap();
}

#[test]
fn failed_publication_removes_staging_file_and_can_be_retried() {
    let root = crate::tests::scratch("game-config-failed-publication");
    let installed = game(&root, "installed");
    let config = root.join("config/game.toml");
    fs::create_dir_all(&config).unwrap();
    let marker = config.join("keep");
    fs::write(&marker, "unchanged").unwrap();
    for _ in 0..2 {
        save(&installed, &config).unwrap_err();
        assert_eq!(fs::read_dir(config.parent().unwrap()).unwrap().count(), 1);
        assert_eq!(fs::read_to_string(&marker).unwrap(), "unchanged");
    }
    fs::remove_dir_all(&config).unwrap();
    save(&installed, &config).unwrap();
    assert_eq!(
        resolve(None, None, || Ok(config.clone()), &[]).unwrap(),
        installed
    );
    fs::remove_dir_all(root).unwrap();
}
