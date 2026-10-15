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
    let a = parsed(&["-Verbose", "-NoCrashDialog"]);
    assert_eq!(a.rest, vec!["-Verbose", "-NoCrashDialog"]);
}

#[test]
fn launch_arguments_select_the_profile_and_console_log() {
    assert_eq!(
        game_argv(Path::new("/g/RimWorld"), Path::new("/p"), &[]),
        vec!["/g/RimWorld", "-savedatafolder=/p", "-NoFileLog"]
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
    let a = parsed(&["--reset", "--", "--game", "-Verbose"]);
    assert!(a.reset);
    assert_eq!(a.game, None);
    assert_eq!(a.rest, vec!["--game", "-Verbose"]);
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
        Path::new("/g/RimWorld"),
        Path::new("/p"),
        &["-Verbose".into()],
    );
    assert_eq!(
        argv,
        vec![
            "/g/RimWorld",
            "-savedatafolder=/p",
            "-NoFileLog",
            "-Verbose"
        ]
    );
}

#[test]
fn a_game_directory_without_the_binary_is_named_rather_than_skipped() {
    let e = game_dir(Some("/nowhere/at/all")).unwrap_err();
    assert!(e.contains("/nowhere/at/all"), "{e}");
}

#[test]
fn game_console_streams_share_the_log_and_each_launch_replaces_it() {
    let root = scratch("console-log");
    let log = root.join("nested/Player.log");
    for _ in 0..2 {
        let mut command = Command::new("sh");
        command.args(["-c", "printf 'output\n'; printf 'error\n' >&2"]);
        configure_game_log(&mut command, &log).unwrap();
        assert!(command.status().unwrap().success());
        assert_eq!(std::fs::read_to_string(&log).unwrap(), "output\nerror\n");
    }
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn invalid_log_directory_reports_its_path() {
    let root = scratch("invalid-log");
    std::fs::create_dir_all(&root).unwrap();
    let parent = root.join("file");
    std::fs::write(&parent, "keep").unwrap();
    let error =
        configure_game_log(&mut Command::new("sh"), &parent.join("Player.log")).unwrap_err();
    assert!(error.contains(parent.to_str().unwrap()), "{error}");
    assert_eq!(std::fs::read_to_string(&parent).unwrap(), "keep");
    std::fs::remove_dir_all(root).unwrap();
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
        assert_eq!(
            game_config::config_path().unwrap(),
            root.join("config/game.toml")
        );
        assert_eq!(
            profile_dir(None, application_paths::Mode::Native).unwrap(),
            root.join("data/profile")
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
            mode.query("profile").unwrap(),
            root.join("data/sidecar/profile")
        );
        assert_eq!(
            mode.query("game-log").unwrap(),
            root.join("cache/sidecar/Player.log")
        );
        assert!(sidecar_paths(mode).is_err());
        let endpoint = root.join("config/sidecar/endpoint.toml");
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
    run_launcher(&args()).unwrap();
    assert!(!profile.join(MARKER).exists());
    let mut init_print = args();
    init_print.init_profile = true;
    assert!(
        run_launcher(&init_print)
            .unwrap_err()
            .contains("cannot be combined")
    );
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

#[test]
fn linux_launch_selects_x11_for_window_manager_fullscreen() {
    let mut command = Command::new("/g/RimWorld");
    configure_game_window(&mut command);
    let selected = command.get_envs().find(|(key, _)| *key == "FORCE_X11");
    if cfg!(target_os = "linux") {
        assert_eq!(
            selected,
            Some((OsStr::new("FORCE_X11"), Some(OsStr::new("1"))))
        );
    } else {
        assert_eq!(selected, None);
    }
}
