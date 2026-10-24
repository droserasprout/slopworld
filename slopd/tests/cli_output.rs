//! Exercise color policy at the CLI process boundary, independently of runtime state.

#[path = "support/cli_output.rs"]
mod support;

const CLIENTS: &[&str] = &[
    env!("CARGO_BIN_EXE_slopctl"),
    env!("CARGO_BIN_EXE_slopworld"),
];

#[test]
fn help_and_errors_preserve_styling_and_respect_no_color() {
    for executable in CLIENTS.iter().copied().chain([env!("CARGO_BIN_EXE_slopd")]) {
        support::assert_diagnostic(executable, &["--help"], true, "Usage:");
        support::assert_diagnostic(executable, &["--unknown"], false, "unexpected argument");
    }
    for args in [&[][..], &["task", "wait", "--help"]] {
        support::assert_diagnostic(env!("CARGO_BIN_EXE_slopctl"), args, true, "Usage:");
    }
    support::assert_diagnostic(env!("CARGO_BIN_EXE_slopctl"), &["task"], false, "Usage:");
    for args in [&["mod"][..], &["profile", "init", "--help"]] {
        support::assert_diagnostic(env!("CARGO_BIN_EXE_slopworld"), args, true, "Usage:");
    }
}

#[test]
fn completion_scripts_versions_and_json_remain_plain_when_color_is_forced() {
    for executable in CLIENTS {
        for shell in ["bash", "zsh", "fish"] {
            support::assert_plain(executable, &["completions", shell]);
        }
        support::assert_plain(executable, &["--version"]);
    }
    support::assert_plain(env!("CARGO_BIN_EXE_slopctl"), &["--json", "paths", "data"]);
}
