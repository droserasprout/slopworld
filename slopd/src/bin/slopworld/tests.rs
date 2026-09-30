use super::*;
use std::sync::atomic::{AtomicU32, Ordering};

pub(super) fn scratch(what: &str) -> PathBuf {
    static N: AtomicU32 = AtomicU32::new(0);
    let n = N.fetch_add(1, Ordering::Relaxed);
    let dir =
        std::env::temp_dir().join(format!("slopworld-test-{}-{what}-{n}", std::process::id()));
    drop(std::fs::remove_dir_all(&dir));
    dir
}

fn parsed(args: &[&str]) -> Args {
    let owned: Vec<String> = args.iter().map(|s| s.to_string()).collect();
    parse(&owned).expect("parses").expect("not help")
}

#[test]
fn a_value_comes_inline_or_after() {
    assert_eq!(parsed(&["--game=/a"]).game.as_deref(), Some("/a"));
    assert_eq!(parsed(&["--game", "/a"]).game.as_deref(), Some("/a"));
    assert_eq!(parsed(&["--profile=/p"]).profile.as_deref(), Some("/p"));
}

/// Forward game flags with one leading hyphen without requiring a separator.
#[test]
fn the_games_arguments_are_passed_through() {
    let a = parsed(&["-popupwindow", "-force-opengl"]);
    assert_eq!(a.rest, vec!["-popupwindow", "-force-opengl"]);
}

#[test]
fn the_window_fix_is_enabled_by_default() {
    let a = game_argv(Path::new("/g/RimWorldLinux"), Path::new("/p"), &[], false);
    assert_eq!(
        a,
        vec![
            "/g/RimWorldLinux",
            "-savedatafolder=/p",
            "-popupwindow",
            "-screen-fullscreen",
            "0",
            "-force-opengl"
        ]
    );
}

#[test]
fn the_window_fix_can_be_disabled() {
    let a = parsed(&["--no-window-fix"]);
    assert!(a.no_window_fix);
    assert_eq!(
        game_argv(
            Path::new("/g/RimWorldLinux"),
            Path::new("/p"),
            &[],
            a.no_window_fix
        ),
        vec!["/g/RimWorldLinux", "-savedatafolder=/p"]
    );
}

/// Reject an incorrect launcher option instead of forwarding it to the game.
#[test]
fn an_unknown_long_option_is_a_mistake_rather_than_a_game_argument() {
    let owned = vec!["--profil".to_string(), "/p".to_string()];
    parse(&owned).unwrap_err();
}

#[test]
fn a_double_dash_ends_our_options() {
    let a = parsed(&["--reset", "--", "--game", "-popupwindow"]);
    assert!(a.reset);
    assert_eq!(a.game, None);
    assert_eq!(a.rest, vec!["--game", "-popupwindow"]);
}

#[test]
fn help_is_not_a_run() {
    assert_eq!(parse(&["--help".to_string()]), Ok(None));
}

#[test]
fn a_missing_value_is_refused() {
    parse(&["--game".to_string()]).unwrap_err();
}

#[test]
fn explicit_profiles_take_precedence_over_sidecar_native_and_xdg_defaults() {
    assert_eq!(
        profile_dir_from(
            Some("/explicit"),
            Some("/sidecar"),
            Some("/native"),
            Some("/data")
        )
        .unwrap(),
        PathBuf::from("/explicit")
    );
    assert_eq!(
        profile_dir_from(None, Some("/sidecar"), Some("/native"), Some("/data")).unwrap(),
        PathBuf::from("/sidecar")
    );
    assert_eq!(
        profile_dir_from(None, None, Some("/native"), Some("/data")).unwrap(),
        PathBuf::from("/native")
    );
    assert_eq!(
        profile_dir_from(None, None, None, Some("/data")).unwrap(),
        PathBuf::from("/data/slopworld/profile")
    );
}

#[test]
fn sidecar_endpoint_is_required_and_must_exist() {
    assert!(sidecar_paths_from(Some("/profile"), None).is_err());
    let root = scratch("endpoint");
    std::fs::create_dir_all(&root).unwrap();
    let endpoint = root.join("endpoint.toml");
    assert!(sidecar_paths_from(Some("/profile"), endpoint.to_str()).is_err());
    std::fs::write(&endpoint, "url = \"http://127.0.0.1:7718\"\n").unwrap();
    assert_eq!(
        sidecar_paths_from(Some("/profile"), endpoint.to_str())
            .unwrap()
            .unwrap()
            .endpoint,
        endpoint
    );
    drop(std::fs::remove_dir_all(root));
}

#[test]
fn an_explicit_mac_game_uses_the_app_mods_directory() {
    let root = scratch("mac-game");
    let app = root.join("RimWorld.app");
    let executable = app.join("Contents/MacOS/RimWorld by Ludeon Studios");
    std::fs::create_dir_all(executable.parent().unwrap()).unwrap();
    std::fs::write(&executable, "game").unwrap();
    let (found, mods) = game_target(None, executable.to_str(), None).unwrap();
    assert_eq!(found, executable);
    assert_eq!(mods, app.join("Mods"));
    drop(std::fs::remove_dir_all(root));
}

#[test]
fn seeding_writes_a_marker_and_a_mod_list() {
    let p = scratch("seed");
    seed(&p, false, false).expect("seeds");
    assert!(p.join(MARKER).is_file(), "the mod looks for this one");
    let xml = std::fs::read_to_string(p.join("Config/ModsConfig.xml")).expect("written");
    let active = xml
        .split("<activeMods>")
        .nth(1)
        .unwrap()
        .split("</activeMods>")
        .next()
        .unwrap();
    assert!(active.contains(&format!("<li>{CORE}</li>")));
    assert!(active.contains(&format!("<li>{SLOPWORLD}</li>")));
    let known = xml
        .split("<knownExpansions>")
        .nth(1)
        .unwrap()
        .split("</knownExpansions>")
        .next()
        .unwrap();
    assert!(
        !p.join("Config/SlopWorld.toml").exists(),
        "native profiles keep their regular UI default"
    );
    for e in EXPANSIONS {
        assert!(
            known.contains(&format!("<li>{e}</li>")),
            "{e} is known, so it is never offered"
        );
    }
    drop(std::fs::remove_dir_all(&p));
}

/// Omit the version to prevent the game from resetting a mismatched mod list and enabling every expansion.
#[test]
fn the_mod_list_states_no_version() {
    assert!(!mods_config_xml().contains("<version>"));
}

#[test]
fn seeding_twice_does_not_overwrite_a_list_somebody_edited() {
    let p = scratch("idempotent");
    seed(&p, false, false).expect("seeds");
    let mods = p.join("Config/ModsConfig.xml");
    std::fs::write(&mods, "<ModsConfigData />").expect("edited by hand");
    seed(&p, false, false).expect("seeds again");
    assert_eq!(
        std::fs::read_to_string(&mods).unwrap(),
        "<ModsConfigData />"
    );
    seed(&p, true, false).expect("resets");
    assert!(std::fs::read_to_string(&mods).unwrap().contains(SLOPWORLD));
    drop(std::fs::remove_dir_all(&p));
}

#[test]
fn sidecar_seeding_defaults_to_warm_without_overwriting_settings() {
    let p = scratch("sidecar-settings");
    seed(&p, false, true).expect("seeds sidecar");
    let settings = p.join("Config/SlopWorld.toml");
    assert_eq!(
        std::fs::read_to_string(&settings).expect("written settings"),
        SIDECAR_UI_SETTINGS
    );

    std::fs::write(&settings, "uiScheme = \"slopworld\"\n").expect("edited by hand");
    seed(&p, false, true).expect("seeds sidecar again");
    assert_eq!(
        std::fs::read_to_string(&settings).unwrap(),
        "uiScheme = \"slopworld\"\n"
    );
    drop(std::fs::remove_dir_all(&p));
}

/// Restore a missing marker so the mod can detect an existing profile.
#[test]
fn a_missing_marker_is_put_back() {
    let p = scratch("marker");
    seed(&p, false, false).expect("seeds");
    std::fs::remove_file(p.join(MARKER)).expect("removed");
    seed(&p, false, false).expect("seeds again");
    assert!(p.join(MARKER).is_file());
    drop(std::fs::remove_dir_all(&p));
}

#[test]
fn the_profile_rides_on_the_games_own_argument() {
    let argv = game_argv(
        Path::new("/g/RimWorldLinux"),
        Path::new("/p"),
        &["-popupwindow".to_string()],
        false,
    );
    assert_eq!(
        argv,
        vec![
            "/g/RimWorldLinux",
            "-savedatafolder=/p",
            "-popupwindow",
            "-screen-fullscreen",
            "0",
            "-force-opengl",
            "-popupwindow"
        ]
    );
}

#[test]
fn a_game_directory_without_the_binary_is_named_rather_than_skipped() {
    let e = game_dir(Some("/nowhere/at/all")).unwrap_err();
    assert!(e.contains("/nowhere/at/all"), "{e}");
}

#[test]
fn a_relative_profile_is_made_absolute() {
    let p = profile_dir(Some("some/where")).expect("resolves");
    assert!(p.is_absolute(), "{}", p.display());
    assert!(p.ends_with("some/where"));
}

#[test]
fn print_reset_and_invalid_working_dir_preserve_profile() {
    let root = scratch("launch-validation");
    let game = root.join("game");
    std::fs::create_dir_all(game.join("Mods/SlopWorld/About")).unwrap();
    std::fs::write(game.join(EXE), "fixture").unwrap();
    std::fs::write(game.join("Mods/SlopWorld/About/About.xml"), "fixture").unwrap();
    let profile = root.join("profile");
    seed(&profile, false, false).unwrap();
    let mods = profile.join("Config/ModsConfig.xml");
    let custom = b"<custom>edited mod list</custom>\n";
    std::fs::write(&mods, custom).unwrap();
    let args = || Args {
        game: Some(game.to_string_lossy().into_owned()),
        profile: Some(profile.to_string_lossy().into_owned()),
        reset: true,
        print: true,
        ..Args::default()
    };
    std::fs::remove_file(profile.join(MARKER)).unwrap();
    run_launcher(args()).unwrap();
    assert!(!profile.join(MARKER).exists());
    let mut init_print = args();
    init_print.init_profile = true;
    assert!(run_launcher(init_print)
        .unwrap_err()
        .contains("cannot be combined"));
    let mut absent = args();
    let absent_profile = root.join("absent-profile");
    absent.profile = Some(absent_profile.to_string_lossy().into_owned());
    run_launcher(absent).unwrap();
    assert!(!absent_profile.exists());
    assert_eq!(std::fs::read(&mods).unwrap(), custom);
    let mut invalid = args();
    invalid.print = false;
    invalid.working_dir = Some(root.join("missing").to_string_lossy().into_owned());
    assert!(run_launcher(invalid)
        .unwrap_err()
        .contains("working directory"));
    assert_eq!(std::fs::read(&mods).unwrap(), custom);
    std::fs::remove_dir_all(root).unwrap();
}
