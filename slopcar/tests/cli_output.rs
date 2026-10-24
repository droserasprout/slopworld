//! Sidecar help and diagnostics use the same color policy as the native clients.

#[path = "../../slopd/tests/support/cli_output.rs"]
mod support;

const EXECUTABLE: &str = env!("CARGO_BIN_EXE_slopcar");

#[test]
fn help_and_errors_preserve_styling_and_respect_no_color() {
    for args in [
        &[][..],
        &["--help"],
        &["start", "--help"],
        &["help", "logs"],
    ] {
        support::assert_diagnostic(EXECUTABLE, args, true, "Usage:");
    }
    support::assert_diagnostic(EXECUTABLE, &["--unknown"], false, "unexpected argument");
    support::assert_diagnostic(
        EXECUTABLE,
        &["start", "--port", "invalid"],
        false,
        "invalid value",
    );
}

#[test]
fn completion_scripts_and_versions_remain_plain_when_color_is_forced() {
    for shell in ["bash", "zsh", "fish"] {
        support::assert_plain(EXECUTABLE, &["completions", shell]);
    }
    support::assert_plain(EXECUTABLE, &["--version"]);
}
