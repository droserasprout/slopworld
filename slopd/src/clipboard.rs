//! The host's clipboard, on the game's behalf. The mod cannot reach it: RimWorld is a Unity
//! player, and `systemCopyBuffer` there is the process's own buffer as often as the desktop's.
//! Which tool does it is the desktop's business - anything missing is skipped, and that is not
//! an error until every one of them is.

use std::process::Stdio;
use std::time::Duration;

use anyhow::{bail, Result};
use tokio::io::AsyncWriteExt;
use tokio::process::Command;

/// The mod's HTTP client gives up at five seconds, so an answer has to beat that;
/// a compositor or an X server can be wedged.
const TIMEOUT: Duration = Duration::from_secs(3);

struct Tool {
    copy: &'static [&'static str],
    // Agent terminals get the original selection so image clipboard data remains available.
    paste: &'static [&'static str],
    // Host shells must only receive textual clipboard data.
    paste_text: &'static [&'static str],
    primary: &'static [&'static str],
    primary_text: &'static [&'static str],
}

/// Try Wayland first: a compositor's clipboard is what its Xwayland clients
/// read too, while the X11 tools see only the Xwayland half. Do not prefilter
/// by DISPLAY/WAYLAND_DISPLAY: slopd is a user service and those variables may
/// be absent even when the installed tool can use its platform default.
const TOOLS: &[Tool] = &[
    Tool {
        copy: &["wl-copy"],
        paste: &["wl-paste", "--no-newline"],
        paste_text: &["wl-paste", "--type", "text", "--no-newline"],
        primary: &["wl-paste", "--primary", "--no-newline"],
        primary_text: &["wl-paste", "--primary", "--type", "text", "--no-newline"],
    },
    Tool {
        copy: &["xclip", "-selection", "clipboard", "-in"],
        paste: &["xclip", "-selection", "clipboard", "-out"],
        paste_text: &[
            "xclip",
            "-selection",
            "clipboard",
            "-target",
            "UTF8_STRING",
            "-out",
        ],
        primary: &["xclip", "-selection", "primary", "-out"],
        primary_text: &[
            "xclip",
            "-selection",
            "primary",
            "-target",
            "UTF8_STRING",
            "-out",
        ],
    },
    Tool {
        copy: &["xsel", "--clipboard", "--input"],
        paste: &["xsel", "--clipboard", "--output"],
        paste_text: &["xsel", "--clipboard", "--output"],
        primary: &["xsel", "--primary", "--output"],
        primary_text: &["xsel", "--primary", "--output"],
    },
];

/// "No such binary" means try the next tool rather than give up: a host with
/// `xclip` and no `xsel` is the ordinary case.
fn missing(e: &anyhow::Error) -> bool {
    e.downcast_ref::<std::io::Error>()
        .map(|io| io.kind() == std::io::ErrorKind::NotFound)
        .unwrap_or(false)
}

pub async fn write(text: &str) -> Result<()> {
    run(text.into(), |t| t.copy).await.map(|_| ())
}

pub async fn read() -> Result<String> {
    run(None, |t| t.paste).await
}

/// Read only text for delivery to a host shell. Keep the unrestricted [`read`] path for agents,
/// whose terminal programs may consume image clipboard data themselves.
pub async fn read_text() -> Result<String> {
    run(None, |t| t.paste_text).await
}

/// Read the compositor's PRIMARY selection, used by terminal middle-click paste.
/// Keep it separate from [`read`]: PRIMARY and CLIPBOARD are independent selections
/// under Wayland, and middle-click must never silently paste the ordinary clipboard.
pub async fn read_primary() -> Result<String> {
    run(None, |t| t.primary).await
}

/// Text-only counterpart to [`read_primary`] for host-shell middle-click paste.
pub async fn read_primary_text() -> Result<String> {
    run(None, |t| t.primary_text).await
}

/// `text` goes down the tool's stdin on a copy; a paste passes `None`.
async fn run(text: Option<&str>, pick: fn(&Tool) -> &'static [&'static str]) -> Result<String> {
    let mut last: Option<anyhow::Error> = None;
    for tool in TOOLS {
        let argv = pick(tool);
        match tokio::time::timeout(TIMEOUT, one(argv, text)).await {
            Err(_) => last = Some(anyhow::anyhow!("{} timed out", argv[0])),
            Ok(Ok(out)) => return Ok(out),
            Ok(Err(e)) => {
                if !missing(&e) {
                    last = Some(e);
                }
            }
        }
    }
    match last {
        Some(e) => Err(e),
        None => bail!("no clipboard tool on this host (wl-copy, xclip or xsel)"),
    }
}

async fn one(argv: &[&str], text: Option<&str>) -> Result<String> {
    let Some(t) = text else {
        return paste(argv).await;
    };

    // A copy tool keeps *serving* the selection after reading it, forking a holder and letting
    // the parent exit - so the output is never collected: the fork inherits the pipes and
    // holds them open, and `wait_with_output` would wait forever. The price is that a failure
    // is an exit status rather than a sentence.
    let mut child = Command::new(argv[0])
        .args(&argv[1..])
        .stdin(Stdio::piped())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        // `run` puts the whole operation under a timeout. Dropping a Tokio
        // `Child` otherwise leaves the process running after that timeout.
        .kill_on_drop(true)
        .spawn()?;

    // Closing stdin is what tells the tool the content is complete.
    let mut stdin = child
        .stdin
        .take()
        .ok_or_else(|| anyhow::anyhow!("{}: no stdin", argv[0]))?;
    stdin.write_all(t.as_bytes()).await?;
    drop(stdin);

    let status = child.wait().await?;
    if !status.success() {
        bail!("{} {}", argv[0], status);
    }
    Ok(String::new())
}

async fn paste(argv: &[&str]) -> Result<String> {
    let out = Command::new(argv[0])
        .args(&argv[1..])
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .kill_on_drop(true)
        .output()
        .await?;
    if !out.status.success() {
        bail!(
            "{}: {}",
            argv[0],
            String::from_utf8_lossy(&out.stderr).trim()
        );
    }
    Ok(String::from_utf8_lossy(&out.stdout).into_owned())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn missing_only_matches_command_not_found_errors() {
        let absent = anyhow::Error::new(std::io::Error::from(std::io::ErrorKind::NotFound));
        let denied = anyhow::Error::new(std::io::Error::from(std::io::ErrorKind::PermissionDenied));
        assert!(missing(&absent));
        assert!(!missing(&denied));
        assert!(!missing(&anyhow::anyhow!("not an I/O error")));
    }

    #[test]
    fn paste_tools_preserve_agent_clipboard_data() {
        assert_eq!(TOOLS[0].paste, &["wl-paste", "--no-newline"]);
        assert_eq!(
            TOOLS[1].paste,
            &["xclip", "-selection", "clipboard", "-out"]
        );
        assert_eq!(TOOLS[2].paste, &["xsel", "--clipboard", "--output"]);
    }

    #[test]
    fn primary_tools_select_the_primary_buffer() {
        assert_eq!(TOOLS[0].primary, &["wl-paste", "--primary", "--no-newline"]);
        assert_eq!(
            TOOLS[1].primary,
            &["xclip", "-selection", "primary", "-out"]
        );
        assert_eq!(TOOLS[2].primary, &["xsel", "--primary", "--output"]);
    }

    #[test]
    fn host_paste_tools_request_text_only() {
        assert_eq!(
            TOOLS[0].paste_text,
            &["wl-paste", "--type", "text", "--no-newline"]
        );
        assert_eq!(
            TOOLS[1].paste_text,
            &[
                "xclip",
                "-selection",
                "clipboard",
                "-target",
                "UTF8_STRING",
                "-out"
            ]
        );
        assert_eq!(TOOLS[2].paste_text, &["xsel", "--clipboard", "--output"]);
        assert_eq!(
            TOOLS[0].primary_text,
            &["wl-paste", "--primary", "--type", "text", "--no-newline"]
        );
        assert_eq!(
            TOOLS[1].primary_text,
            &[
                "xclip",
                "-selection",
                "primary",
                "-target",
                "UTF8_STRING",
                "-out"
            ]
        );
        assert_eq!(TOOLS[2].primary_text, &["xsel", "--primary", "--output"]);
    }

    #[tokio::test]
    async fn copy_process_receives_the_complete_text_on_stdin() {
        one(
            &["sh", "-c", "test \"$(cat)\" = clipboard-text"],
            Some("clipboard-text"),
        )
        .await
        .unwrap();
    }

    #[tokio::test]
    async fn paste_process_returns_stdout_verbatim() {
        let output = paste(&["sh", "-c", "printf 'first\\nsecond'"])
            .await
            .unwrap();
        assert_eq!(output, "first\nsecond");
    }

    #[tokio::test]
    async fn primary_process_returns_stdout_verbatim() {
        let output = one(&["sh", "-c", "printf primary-text"], None)
            .await
            .unwrap();
        assert_eq!(output, "primary-text");
    }

    #[tokio::test]
    async fn paste_process_reports_stderr_on_failure() {
        let error = paste(&["sh", "-c", "printf 'clipboard unavailable' >&2; exit 7"])
            .await
            .unwrap_err()
            .to_string();
        assert!(error.contains("clipboard unavailable"), "{error}");
    }
}
