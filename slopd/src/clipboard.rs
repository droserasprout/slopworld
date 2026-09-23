//! Access the host clipboard for the game.
//! Unity's `systemCopyBuffer` can refer to a process buffer instead of the desktop clipboard.
//! Try the available desktop tools. Skip missing tools and report an error if none succeeds.

use std::process::Stdio;
use std::time::Duration;

use anyhow::{bail, Result};
use tokio::io::AsyncWriteExt;
use tokio::process::Command;

/// Finish before the mod's five-second HTTP timeout, even if the compositor or X server is unresponsive.
const TIMEOUT: Duration = Duration::from_secs(3);

struct Tool {
    copy: &'static [&'static str],
    primary_copy: &'static [&'static str],
    // Agent terminals request the original selection.
    // Discard binary results before the HTTP handler converts the result to text.
    paste: &'static [&'static str],
    // Host shells must only receive textual clipboard data.
    paste_text: &'static [&'static str],
    primary: &'static [&'static str],
    primary_text: &'static [&'static str],
}

/// Try Wayland first because Xwayland clients also read the compositor clipboard.
/// X11 tools can access only the Xwayland clipboard.
/// Do not filter tools by DISPLAY or WAYLAND_DISPLAY.
/// These variables can be absent in the slopd service even when tools can use platform defaults.
const TOOLS: &[Tool] = &[
    Tool {
        copy: &["wl-copy"],
        primary_copy: &["wl-copy", "--primary"],
        paste: &["wl-paste", "--no-newline"],
        paste_text: &["wl-paste", "--type", "text", "--no-newline"],
        primary: &["wl-paste", "--primary", "--no-newline"],
        primary_text: &["wl-paste", "--primary", "--type", "text", "--no-newline"],
    },
    Tool {
        copy: &["xclip", "-selection", "clipboard", "-in"],
        primary_copy: &["xclip", "-selection", "primary", "-in"],
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
        primary_copy: &["xsel", "--primary", "--input"],
        paste: &["xsel", "--clipboard", "--output"],
        paste_text: &["xsel", "--clipboard", "--output"],
        primary: &["xsel", "--primary", "--output"],
        primary_text: &["xsel", "--primary", "--output"],
    },
];

/// Check whether a missing executable caused the error.
/// If so, try the next tool because a host might have only some clipboard tools.
fn missing(e: &anyhow::Error) -> bool {
    e.downcast_ref::<std::io::Error>()
        .map(|io| io.kind() == std::io::ErrorKind::NotFound)
        .unwrap_or(false)
}

pub async fn write(text: &str) -> Result<()> {
    run(text.into(), |t| t.copy).await.map(|_| ())
}

/// Write the compositor's PRIMARY selection, used by terminal multi-click selection.
pub async fn write_primary(text: &str) -> Result<()> {
    run(text.into(), |t| t.primary_copy).await.map(|_| ())
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

/// Send `text` to the tool's stdin for a copy. Use `None` for a paste.
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

    // A copy tool can start a child to serve the selection after its parent exits.
    // The child inherits pipes and keeps them open, so wait_with_output could wait indefinitely.
    // Do not collect output. Report failures through exit status.
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
    // Clipboard selections can contain binary data, such as GIF images.
    // Do not replace invalid UTF-8 bytes with text characters.
    // The caller can send the returned string directly to a shell or terminal application in a JSON paste event.
    // This text endpoint accepts UTF-8 and excludes other data.
    Ok(text_output(&out.stdout))
}

fn text_output(bytes: &[u8]) -> String {
    if looks_like_image(bytes) || bytes.contains(&0) {
        return String::new();
    }
    String::from_utf8(bytes.to_vec()).unwrap_or_default()
}

fn looks_like_image(bytes: &[u8]) -> bool {
    bytes.starts_with(b"GIF87a")
        || bytes.starts_with(b"GIF89a")
        || bytes.starts_with(b"\x89PNG\r\n\x1a\n")
        || bytes.starts_with(&[0xff, 0xd8, 0xff])
        || bytes.starts_with(b"BM")
        || (bytes.starts_with(b"RIFF") && bytes.get(8..12) == Some(b"WEBP"))
        || bytes.starts_with(b"II\x2a\x00")
        || bytes.starts_with(b"MM\x00\x2a")
}

#[cfg(test)]
#[path = "clipboard_tests.rs"]
mod tests;
