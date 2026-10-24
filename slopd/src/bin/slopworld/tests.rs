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
    assert_eq!(
        parsed(&["--endpoint=tank"]).endpoint.as_deref(),
        Some("tank")
    );
    assert_eq!(
        parsed(&["--endpoint", "tank"]).endpoint.as_deref(),
        Some("tank")
    );
}

#[test]
fn the_window_fix_is_enabled_by_default() {
    let a = game_argv(
        Path::new("/g/RimWorldLinux"),
        Path::new("/p"),
        Path::new("/logs/Player.log"),
        false,
    );
    assert_eq!(
        a,
        vec![
            "/g/RimWorldLinux",
            "-savedatafolder=/p",
            "-logFile",
            "/logs/Player.log",
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
            Path::new("/logs/Player.log"),
            a.no_window_fix
        ),
        vec![
            "/g/RimWorldLinux",
            "-savedatafolder=/p",
            "-logFile",
            "/logs/Player.log"
        ]
    );
}

/// Reject an incorrect launcher option instead of forwarding it to the game.
#[test]
fn an_unknown_long_option_is_a_mistake_rather_than_a_game_argument() {
    let owned = vec!["--profil".to_string(), "/p".to_string()];
    parse(&owned).unwrap_err();
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
fn explicit_profiles_select_storage_without_selecting_a_mode() {
    for mode in [
        application_paths::Mode::Native,
        application_paths::Mode::Sidecar,
    ] {
        assert_eq!(
            profile_dir(Some("/explicit"), mode).unwrap(),
            PathBuf::from("/explicit")
        );
    }
    assert!(
        sidecar_paths(application_paths::Mode::Native)
            .unwrap()
            .is_none()
    );
}

#[test]
fn sidecar_endpoint_must_exist_and_is_resolved_to_an_absolute_path() {
    let root = scratch("endpoint");
    std::fs::create_dir_all(&root).unwrap();
    let endpoint = root.join("endpoint.toml");
    assert!(sidecar_paths_from(&endpoint).is_err());
    std::fs::write(&endpoint, "url = \"http://127.0.0.1:7718\"\n").unwrap();
    assert_eq!(sidecar_paths_from(&endpoint).unwrap().endpoint, endpoint);
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
fn seeding_leaves_game_preferences_to_rimworld_and_preserves_existing_preferences() {
    for sidecar in [false, true] {
        let p = scratch("audio-prefs");
        seed(&p, false, sidecar).expect("seeds audio");
        let prefs = p.join("Config/Prefs.xml");
        assert!(
            !prefs.exists(),
            "RimWorld must run first-time display detection"
        );
        let edited = "<PrefsData><volumeMaster>0.4</volumeMaster></PrefsData>\n";
        std::fs::write(&prefs, edited).unwrap();
        for reset in [false, true] {
            seed(&p, reset, sidecar).expect("preserves audio");
            assert_eq!(std::fs::read_to_string(&prefs).unwrap(), edited);
        }
        std::fs::remove_dir_all(p).unwrap();
    }
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
        Path::new("/logs/Player.log"),
        false,
    );
    assert_eq!(
        argv,
        vec![
            "/g/RimWorldLinux",
            "-savedatafolder=/p",
            "-logFile",
            "/logs/Player.log",
            "-popupwindow",
            "-screen-fullscreen",
            "0",
            "-force-opengl"
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
    let p = profile_dir(Some("some/where"), application_paths::Mode::Native).expect("resolves");
    assert!(p.is_absolute(), "{}", p.display());
    assert!(p.ends_with("some/where"));
}

#[test]
fn launcher_defaults_follow_roots_and_print_creates_no_state() {
    const MARKER_ENV: &str = "SLOPWORLD_ROOT_TEST";
    if let Some(root) = std::env::var_os(MARKER_ENV) {
        let root = PathBuf::from(root);
        assert_default_root_paths(&root);
        assert_named_profile_paths(&root);
        return;
    }
    // Set environment before the child harness starts threads, avoiding global
    // environment races while exercising the actual launcher entry points.
    let root = scratch("application-roots");
    let name = std::thread::current().name().unwrap().to_owned();
    let output = Command::new(std::env::current_exe().unwrap())
        .args(["--exact", &name, "--nocapture"])
        .env(MARKER_ENV, &root)
        .env("SLOPD_CONFIG_ROOT", root.join("config"))
        .env("SLOPD_DATA", root.join("data"))
        .env("SLOPD_CACHE", root.join("cache"))
        .env("XDG_CONFIG_HOME", root.join("unused-xdg"))
        .env("XDG_DATA_HOME", root.join("unused-xdg"))
        .env("XDG_CACHE_HOME", root.join("unused-xdg"))
        .env_remove("SLOPWORLD_PROFILE")
        .env_remove("SLOPWORLD_GAME_LOG")
        .env_remove("SLOPD_ENDPOINT")
        .output()
        .unwrap();
    drop(std::fs::remove_dir_all(root));
    assert!(
        output.status.success(),
        "{}\n{}",
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr)
    );
}

fn assert_default_root_paths(root: &Path) {
    assert_eq!(
        game_config::config_path().unwrap(),
        root.join("config/game.toml")
    );
    assert_eq!(
        profile_dir(None, application_paths::Mode::Native).unwrap(),
        root.join("data/profiles/local")
    );
    assert_eq!(
        application_paths::Mode::Native.game_log_path(),
        root.join("cache/Player.log")
    );
    let game = root.join("game");
    std::fs::create_dir_all(game.join("Mods/SlopWorld/About")).unwrap();
    std::fs::write(game.join(EXE), "fixture").unwrap();
    std::fs::write(game.join("Mods/SlopWorld/About/About.xml"), "fixture").unwrap();
    run_launcher(&Args {
        game: Some(game.to_string_lossy().into_owned()),
        print: true,
        ..Args::default()
    })
    .unwrap();
    for name in ["config", "data", "cache", "unused-xdg"] {
        assert!(!root.join(name).exists(), "print created {name}");
    }
    let mode = application_paths::Mode::Sidecar;
    assert_eq!(
        mode.query("profile", None).unwrap(),
        root.join("data/sidecar/profiles/local")
    );
    assert_eq!(
        mode.query("game-log", None).unwrap(),
        root.join("cache/sidecar/Player.log")
    );
    assert!(sidecar_paths(mode).is_err());
    let endpoint = root.join("config/sidecar/endpoints/local.toml");
    std::fs::create_dir_all(endpoint.parent().unwrap()).unwrap();
    std::fs::write(&endpoint, "fixture").unwrap();
    assert_eq!(sidecar_paths(mode).unwrap().unwrap().endpoint, endpoint);
    run_launcher(&Args {
        game: Some(game.to_string_lossy().into_owned()),
        sidecar: true,
        print: true,
        ..Args::default()
    })
    .unwrap();
    for name in ["data", "cache", "unused-xdg"] {
        assert!(!root.join(name).exists(), "sidecar print created {name}");
    }
}

fn assert_named_profile_paths(root: &Path) {
    let mode = application_paths::Mode::Sidecar;
    // Named connections initialize independently, even while their daemons are offline.
    for name in ["tank", "other"] {
        let initialize = || {
            run_profile(
                cli::ProfileCommand::Init(cli::ProfileOptions::default()),
                application_paths::Mode::Native,
                Some(name),
            )
        };
        initialize().unwrap();
        let profile = root.join("data/profiles").join(name);
        assert!(profile.join(MARKER).is_file());
        assert_eq!(
            mode.query("profile", Some(name)).unwrap(),
            root.join("data/sidecar/profiles").join(name)
        );
        assert_eq!(
            application_paths::Mode::Native
                .query("endpoint", Some(name))
                .unwrap(),
            root.join("config/endpoints").join(format!("{name}.toml"))
        );
        let mods = profile.join("Config/ModsConfig.xml");
        std::fs::write(&mods, "custom mods").unwrap();
        initialize().unwrap();
        assert_eq!(std::fs::read_to_string(mods).unwrap(), "custom mods");
        let missing = parsed(&["--endpoint", name, "--print"]);
        assert!(
            run_launcher(&missing)
                .unwrap_err()
                .contains("endpoint descriptor does not exist")
        );
    }
    std::fs::write(root.join("config/client.toml"), "endpoint = 'tank'\n").unwrap();
    assert_eq!(
        profile_dir(None, application_paths::Mode::Native).unwrap(),
        root.join("data/profiles/local")
    );
    assert_eq!(
        application_paths::Mode::Native
            .endpoint_path_for(None)
            .unwrap(),
        root.join("config/endpoints/local.toml")
    );
    // Obsolete client settings cannot redirect the default connection.
    assert_eq!(
        application_paths::Mode::Native.endpoint_path(),
        root.join("config/endpoints/local.toml")
    );
    std::fs::write(root.join("config/client.toml"), "endpoint = [").unwrap();
    application_paths::Mode::Native
        .endpoint_path_for(None)
        .unwrap();
    assert_eq!(
        application_paths::Mode::Native
            .endpoint_path_for(Some("tank"))
            .unwrap(),
        root.join("config/endpoints/tank.toml")
    );
    assert!(!root.join("data/profile").exists());
    assert!(!root.join("data/sidecar/profile").exists());
    assert_eq!(
        profile_dir_for(
            Some("/explicit"),
            mode,
            Path::new("/config/endpoints/tank.toml")
        )
        .unwrap(),
        PathBuf::from("/explicit")
    );
}

#[test]
fn print_and_invalid_working_dir_preserve_profile() {
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
        print: true,
        ..Args::default()
    };
    std::fs::remove_file(profile.join(MARKER)).unwrap();
    run_launcher(&args()).unwrap();
    assert!(!profile.join(MARKER).exists());
    let mut absent = args();
    let absent_profile = root.join("absent-profile");
    absent.profile = Some(absent_profile.to_string_lossy().into_owned());
    run_launcher(&absent).unwrap();
    assert!(!absent_profile.exists());
    assert_eq!(std::fs::read(&mods).unwrap(), custom);
    let mut invalid = args();
    invalid.print = false;
    invalid.working_dir = Some(root.join("missing").to_string_lossy().into_owned());
    assert!(
        run_launcher(&invalid)
            .unwrap_err()
            .contains("working directory")
    );
    assert_eq!(std::fs::read(&mods).unwrap(), custom);
    std::fs::remove_dir_all(root).unwrap();
}

fn parse(args: &[String]) -> Result<Option<Args>, String> {
    match crate::cli::parse(args.to_vec()).map_err(|error| error.to_string())? {
        crate::cli::Command::Launch(args) => Ok(Some(args)),
        crate::cli::Command::Display(_) => Ok(None),
        _ => Err("expected launch".into()),
    }
}
