use super::{Args, MOD_DIRS, MOD_NAME, USAGE, ensure_non_overlapping, install, parse, try_run};
use std::fs;
use std::path::PathBuf;
use std::sync::atomic::{AtomicU32, Ordering};

fn scratch(name: &str) -> PathBuf {
    static NEXT: AtomicU32 = AtomicU32::new(0);
    let path = std::env::temp_dir().join(format!(
        "slopworld-mod-install-test-{}-{name}-{}",
        std::process::id(),
        NEXT.fetch_add(1, Ordering::Relaxed)
    ));
    drop(fs::remove_dir_all(&path));
    fs::create_dir_all(&path).unwrap();
    path
}

fn args(parts: &[&str]) -> Vec<String> {
    parts.iter().map(|part| (*part).to_string()).collect()
}

fn mod_source(root: &std::path::Path) -> PathBuf {
    let source = root.join("source");
    for directory in MOD_DIRS {
        fs::create_dir_all(source.join(directory)).unwrap();
    }
    fs::write(source.join("About/About.xml"), "fixture").unwrap();
    source
}

#[test]
fn successful_linux_mod_install_remembers_game_and_failed_install_preserves_it() {
    let root = scratch("remember-game");
    let source = mod_source(&root);
    let config = root.join("config/game.toml");
    let mut games = Vec::new();
    for name in ["first game", "second game"] {
        let game = root.join(name);
        let mods = game.join("Mods");
        fs::create_dir_all(&mods).unwrap();
        fs::write(game.join(crate::EXE), "fixture").unwrap();
        super::install_and_remember(&source, &mods, || Ok(config.clone())).unwrap();
        assert_eq!(
            crate::game_config::resolve(None, None, || Ok(config.clone()), &[]).unwrap(),
            game
        );
        games.push(game);
    }
    let before = fs::read(&config).unwrap();
    fs::remove_dir_all(source.join("About")).unwrap();
    super::install_and_remember(&source, &games[0].join("Mods"), || Ok(config.clone()))
        .unwrap_err();
    assert_eq!(fs::read(&config).unwrap(), before);
    fs::remove_dir_all(root).unwrap();
}

#[test]
fn non_linux_mod_install_does_not_discover_or_change_linux_default() {
    let root = scratch("non-linux-default");
    let source = mod_source(&root);
    let mods = root.join("RimWorld.app/Mods");
    fs::create_dir_all(&mods).unwrap();
    super::install_and_remember(&source, &mods, || {
        panic!("non-Linux installation must bypass config discovery")
    })
    .unwrap();
    assert!(mods.join(MOD_NAME).join("About/About.xml").is_file());
    fs::remove_dir_all(root).unwrap();
}

#[test]
fn config_save_failure_reports_that_the_mod_was_installed() {
    let root = scratch("remember-failure");
    let source = mod_source(&root);
    let mods = root.join("Mods");
    fs::create_dir_all(&mods).unwrap();
    fs::write(root.join(crate::EXE), "fixture").unwrap();
    let config = root.join("game.toml");
    fs::create_dir(&config).unwrap();
    let error = super::install_and_remember(&source, &mods, || Ok(config.clone())).unwrap_err();
    assert!(
        error.contains("mod installed") && error.contains("saving"),
        "{error}"
    );
    assert!(mods.join(MOD_NAME).join("About/About.xml").is_file());
    fs::remove_dir_all(root).unwrap();
}

#[test]
fn command_dispatch_is_opt_in() {
    assert_eq!(try_run(&args(&["--help"])).unwrap(), None);
    assert_eq!(
        try_run(&args(&["mod", "--help"])).unwrap(),
        Some(USAGE.to_string())
    );
    try_run(&args(&["mod", "unknown"])).unwrap_err();
}

#[test]
fn parser_accepts_install_and_uninstall_shapes() {
    assert_eq!(
        parse(&args(&["--source", "/src", "--game=/game"]), true),
        Ok(Args {
            source: Some(PathBuf::from("/src")),
            game: Some(PathBuf::from("/game")),
            ..Default::default()
        })
    );
    assert_eq!(
        parse(&args(&["--game", "/game"]), false),
        Ok(Args {
            game: Some(PathBuf::from("/game")),
            ..Default::default()
        })
    );
}

#[test]
fn install_replaces_only_the_named_mod_and_uninstall_removes_it() {
    let root = scratch("copy");
    let source = root.join("source");
    let mods = root.join("Mods");
    fs::create_dir_all(&source).unwrap();
    fs::create_dir_all(&mods).unwrap();
    for directory in MOD_DIRS {
        fs::create_dir_all(source.join(directory)).unwrap();
    }
    fs::write(source.join("About/About.xml"), "new").unwrap();
    fs::create_dir_all(mods.join(MOD_NAME)).unwrap();
    fs::write(mods.join(MOD_NAME).join("old.txt"), "old").unwrap();
    fs::write(mods.join("Keep.txt"), "keep").unwrap();
    fs::create_dir_all(mods.join("Neighbor")).unwrap();
    let neighbor = mods.join("Neighbor/marker");
    fs::write(&neighbor, "neighbor").unwrap();

    let source = fs::canonicalize(source).unwrap();
    let mods = fs::canonicalize(mods).unwrap();
    try_run(&args(&[
        "mod",
        "install",
        "--source",
        source.to_str().unwrap(),
        "--game",
        root.to_str().unwrap(),
    ]))
    .unwrap();
    assert_eq!(
        fs::read_to_string(mods.join(MOD_NAME).join("About/About.xml")).unwrap(),
        "new"
    );
    assert!(!mods.join(MOD_NAME).join("old.txt").exists());
    assert_eq!(fs::read_to_string(mods.join("Keep.txt")).unwrap(), "keep");

    assert_eq!(fs::read_to_string(&neighbor).unwrap(), "neighbor");
    try_run(&args(&[
        "mod",
        "uninstall",
        "--game",
        root.to_str().unwrap(),
    ]))
    .unwrap();
    assert_eq!(fs::read_to_string(&neighbor).unwrap(), "neighbor");
    assert!(!mods.join(MOD_NAME).exists());
    assert!(mods.join("Keep.txt").exists());
    fs::remove_dir_all(root).unwrap();
}

#[test]
fn installation_cannot_target_the_source_tree() {
    let root = scratch("overlap");
    let source = root.join("source");
    let destination = source.join("Mods").join(MOD_NAME);
    fs::create_dir_all(&destination).unwrap();
    let source = fs::canonicalize(source).unwrap();
    let destination = fs::canonicalize(destination).unwrap();
    assert!(ensure_non_overlapping(&source, &destination).is_err());
    let nested_source = destination.join("source");
    fs::create_dir_all(&nested_source).unwrap();
    fs::write(nested_source.join("marker"), "keep").unwrap();
    install(&nested_source, &source.join("Mods")).unwrap_err();
    assert_eq!(
        fs::read_to_string(nested_source.join("marker")).unwrap(),
        "keep"
    );
    fs::remove_dir_all(root).unwrap();
}

#[test]
fn failed_final_install_restores_the_previous_mod() {
    let root = scratch("rollback");
    let destination = root.join(MOD_NAME);
    fs::create_dir(&destination).unwrap();
    fs::write(destination.join("marker"), "old").unwrap();
    let backup = root.join(".backup");
    let missing_staging = root.join(".missing-staging");
    assert!(super::commit_install(&missing_staging, &destination, &backup).is_err());
    assert_eq!(
        fs::read_to_string(destination.join("marker")).unwrap(),
        "old"
    );
    assert!(!backup.exists());
    fs::remove_dir_all(root).unwrap();
}

#[test]
fn install_game_selection_uses_explicit_then_environment_then_saved_path() {
    let root = scratch("game-selection");
    let config = root.join("game.toml");
    let saved = root.join("saved");
    let environment = root.join("environment");
    let explicit = root.join("explicit");
    for game in [&saved, &environment, &explicit] {
        fs::create_dir_all(game.join("Mods")).unwrap();
        fs::write(game.join(crate::EXE), "fixture").unwrap();
    }
    crate::game_config::save(&saved, &config).unwrap();
    let default_game = || crate::game_config::resolve(None, None, || Ok(config.clone()), &[]);
    for (options, env, expected) in [
        (Args::default(), None, &saved),
        (Args::default(), environment.to_str(), &environment),
        (
            Args {
                game: Some(explicit.clone()),
                ..Default::default()
            },
            environment.to_str(),
            &explicit,
        ),
    ] {
        assert_eq!(
            super::resolve_mods(&options, env, default_game).unwrap(),
            expected.join("Mods")
        );
    }
    let invalid = Args {
        game: Some(root.join("missing")),
        ..Default::default()
    };
    super::resolve_mods(&invalid, environment.to_str(), default_game).unwrap_err();
    super::resolve_mods(&Args::default(), Some(""), default_game).unwrap_err();
    fs::remove_dir_all(root).unwrap();
}

#[test]
fn install_options_are_optional_and_game_accepts_both_value_forms() {
    assert_eq!(parse(&[], true).unwrap(), Args::default());
    for parts in [vec!["--game", "/game"], vec!["--game=/game"]] {
        assert_eq!(
            parse(&args(&parts), true).unwrap(),
            Args {
                game: Some(PathBuf::from("/game")),
                ..Default::default()
            }
        );
    }
    parse(&args(&["--game"]), true).unwrap_err();
}

#[test]
fn install_rejects_the_removed_mods_option() {
    for options in [vec!["--mods", "/mods"], vec!["--mods=/mods"]] {
        let error = parse(&args(&options), true).unwrap_err();
        assert!(error.contains("unknown option --mods"), "{error}");
    }
}

#[test]
fn mac_default_uses_the_app_bundle_and_allows_a_configured_location() {
    assert_eq!(
        super::default_mod_game(true, None).unwrap(),
        PathBuf::from(crate::expand("~/Documents/RimWorld.app"))
    );
    let root = scratch("mac-default");
    let game = root.join("RimWorld.app");
    fs::create_dir_all(game.join("Mods")).unwrap();
    let mods = super::resolve_mods(&Args::default(), None, || {
        super::default_mod_game(true, game.to_str())
    })
    .unwrap();
    assert_eq!(mods, game.join("Mods"));
    fs::remove_dir_all(root).unwrap();
}

#[test]
fn uninstall_accepts_defaults_and_rejects_source_and_legacy_mods() {
    assert_eq!(parse(&[], false).unwrap(), Args::default());
    assert_eq!(
        parse(&args(&["--game=/game"]), false).unwrap().game,
        Some(PathBuf::from("/game"))
    );
    for options in [
        vec!["--mods", "/mods"],
        vec!["--mods=/mods"],
        vec!["--source", "/source"],
        vec!["--source=/source"],
        vec!["--game"],
    ] {
        parse(&args(&options), false).unwrap_err();
    }
}
