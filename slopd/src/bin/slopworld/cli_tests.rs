use super::*;
fn parse_words(words: &[&str]) -> Result<Command, String> {
    parse(words.iter().map(|word| (*word).into()).collect()).map_err(|error| error.to_string())
}

#[test]
fn game_arguments_are_rejected_instead_of_forwarded() {
    for words in [
        vec!["-screen-width", "1200"],
        vec!["--print", "-popupwindow"],
        vec!["--", "-logFile", "/custom/log"],
        vec!["--", "--help"],
        vec!["unknown-command"],
    ] {
        assert!(parse_words(&words).is_err(), "{words:?}");
    }
}

#[test]
fn local_help_requires_no_runtime_state() {
    for words in [
        vec!["--help"],
        vec!["connect", "--help"],
        vec!["endpoints", "--help"],
        vec!["paths", "--help"],
        vec!["mod"],
        vec!["mod", "install", "--help"],
        vec!["mod", "uninstall", "--help"],
        vec!["profile", "--help"],
        vec!["profile", "init", "--help"],
        vec!["profile", "reset", "--help"],
    ] {
        assert!(
            matches!(parse_words(&words).unwrap(), Command::Display(text) if text.to_string().contains("Usage:"))
        );
    }
}

#[test]
fn profile_commands_accept_selection_and_reject_launch_options() {
    for action in ["init", "reset"] {
        let Command::Local {
            command: LocalCommand::Profile { command },
            mode,
            endpoint,
        } = parse_words(&[
            "--sidecar",
            "profile",
            action,
            "--endpoint",
            "tank",
            "--profile",
            "/custom profile",
        ])
        .unwrap()
        else {
            panic!("expected profile command")
        };
        assert_eq!(mode, Mode::Sidecar);
        assert_eq!(endpoint.as_deref(), Some("tank"));
        let options = match (action, command) {
            ("init", ProfileCommand::Init(options)) | ("reset", ProfileCommand::Reset(options)) => {
                options
            }
            other => panic!("unexpected command: {other:?}"),
        };
        assert_eq!(options.profile.as_deref(), Some("/custom profile"));
        for flag in ["--print", "--no-window-fix", "--reset", "--init-profile"] {
            parse_words(&[flag, "profile", action]).unwrap_err();
            parse_words(&["profile", action, flag]).unwrap_err();
        }
    }
    for flag in ["--init-profile", "--reset"] {
        parse_words(&[flag]).unwrap_err();
    }
}

#[test]
fn peer_selection_and_mod_destinations_are_validated() {
    for words in [
        vec!["--endpoint", "../bad"],
        vec!["--endpoint", "one", "--endpoint", "two"],
        vec!["--print", "endpoints"],
        vec!["connect", "tank", "--endpoint", "other"],
        vec!["mod", "install", "--game", "/game", "--mods", "/mods"],
        vec!["mod", "uninstall", "--source", "/source"],
        vec!["endpoints", "extra"],
    ] {
        assert!(parse_words(&words).is_err(), "{words:?}");
    }
    assert!(
        matches!(parse_words(&["paths", "profile", "--sidecar", "--endpoint", "tank"]).unwrap(), Command::Local { mode: Mode::Sidecar, endpoint: Some(name), .. } if name == "tank")
    );
}

#[test]
fn globals_before_commands_select_local_paths() {
    assert!(
        matches!(parse_words(&["--endpoint=tank", "--sidecar", "paths", "profile"]).unwrap(), Command::Local { mode: Mode::Sidecar, endpoint: Some(name), .. } if name == "tank")
    );
}

#[test]
fn command_schema_is_consistent() {
    Cli::command().debug_assert();
}

#[test]
fn help_groups_options_without_advertising_a_help_subcommand() {
    let Command::Display(help) = parse_words(&["--help"]).unwrap() else {
        panic!()
    };
    let help = help.to_string();
    for heading in ["Game:", "Profile:", "Connection:"] {
        assert!(help.contains(heading));
    }
    assert!(!help.contains("Print this message or the help of the given subcommand"));
    assert!(matches!(parse_words(&["-h"]).unwrap(), Command::Display(_)));
}
