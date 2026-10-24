//! Shared subprocess checks for styled diagnostics and plain machine output.

use std::process::{Command, Output};

#[expect(
    clippy::expect_used,
    reason = "Failure to start a CLI process must fail the integration test"
)]
fn capture(executable: &str, args: &[&str], force: bool, no_color: bool) -> Output {
    let mut command = Command::new(executable);
    command
        .args(args)
        .env("TERM", "xterm-256color")
        .env_remove("NO_COLOR")
        .env_remove("CLICOLOR")
        .env_remove("CLICOLOR_FORCE")
        .env_remove("CI");
    if force {
        command.env("CLICOLOR_FORCE", "1");
    }
    if no_color {
        command.env("NO_COLOR", "1");
    }
    command.output().expect("run local CLI request")
}

pub fn assert_diagnostic(executable: &str, args: &[&str], success: bool, expected: &str) {
    let mut plain = None;
    for (force, no_color) in [(false, false), (true, false), (true, true)] {
        let output = capture(executable, args, force, no_color);
        assert_eq!(output.status.success(), success, "{args:?}: {output:?}");
        let (text, other) = if success {
            (&output.stdout, &output.stderr)
        } else {
            (&output.stderr, &output.stdout)
        };
        assert!(other.is_empty(), "{args:?}: {output:?}");
        assert!(
            String::from_utf8_lossy(text).contains(expected),
            "{args:?}: {output:?}"
        );
        assert_eq!(
            text.contains(&0x1b),
            force && !no_color,
            "{args:?}, force={force}, no_color={no_color}: {output:?}"
        );
        if force && !no_color {
            let text = String::from_utf8_lossy(text);
            // Bold/underline alone is not color: protect the actual help palette.
            let heading_color = if text.contains("Usage:") {
                "\x1b[32m"
            } else {
                "\x1b[31m"
            };
            assert!(
                text.contains(heading_color),
                "missing heading color: {text:?}"
            );
            assert!(
                text.contains("\x1b[36m"),
                "missing cyan command/option: {text:?}"
            );
        }
        if !force {
            plain = Some(text.clone());
        } else if no_color {
            assert_eq!(Some(text), plain.as_ref(), "NO_COLOR changed the content");
        }
    }
}

pub fn assert_plain(executable: &str, args: &[&str]) {
    let plain = capture(executable, args, false, false);
    let forced = capture(executable, args, true, false);
    for output in [&plain, &forced] {
        assert!(output.status.success(), "{args:?}: {output:?}");
        assert!(output.stderr.is_empty(), "{args:?}: {output:?}");
        assert!(!output.stdout.is_empty());
        assert!(!output.stdout.contains(&0x1b), "{args:?}: {output:?}");
    }
    assert_eq!(plain.stdout, forced.stdout);
}
