//! The host's clipboard, on the game's behalf.
//!
//! The mod cannot reach it. RimWorld is a Unity player and `systemCopyBuffer`
//! there is the process's own buffer as often as it is the desktop's - which
//! makes a selection copied out of a pane land nowhere a browser can read it,
//! and that is the whole of "copy doesn't work". slopd is on the host with the
//! session's `DISPLAY`/`WAYLAND_DISPLAY` already forwarded to it, so the one
//! thing here the game cannot do is exactly the thing this can.
//!
//! Which tool does it is the desktop's business rather than ours: `wl-copy`
//! when the session is Wayland, `xclip` or `xsel` when it is X11, tried in that
//! order and skipping anything the host does not have. A binary that is missing
//! is not an error until every one of them is.

use std::process::Stdio;
use std::time::Duration;

use anyhow::{bail, Result};
use tokio::io::AsyncWriteExt;
use tokio::process::Command;

/// A tool never gets longer than this. A clipboard command talks to a
/// compositor or an X server and either can be wedged; the mod's HTTP client
/// gives up at five seconds, so an answer has to beat that or the pane reports
/// a failure it could have reported itself.
const TIMEOUT: Duration = Duration::from_secs(3);

/// Which display server a tool needs to be worth trying.
#[derive(PartialEq)]
enum Needs {
    Wayland,
    X11,
}

struct Tool {
    needs: Needs,
    copy: &'static [&'static str],
    paste: &'static [&'static str],
}

/// Wayland first when the session is one: a compositor's own clipboard is what
/// its Xwayland clients read too, so it answers for both, while the X11 tools
/// on a Wayland desktop only ever see the Xwayland half.
const TOOLS: &[Tool] = &[
    Tool {
        needs: Needs::Wayland,
        copy: &["wl-copy"],
        paste: &["wl-paste", "--no-newline"],
    },
    Tool {
        needs: Needs::X11,
        copy: &["xclip", "-selection", "clipboard", "-in"],
        paste: &["xclip", "-selection", "clipboard", "-out"],
    },
    Tool {
        needs: Needs::X11,
        copy: &["xsel", "--clipboard", "--input"],
        paste: &["xsel", "--clipboard", "--output"],
    },
];

fn have(needs: &Needs) -> bool {
    let var = match needs {
        Needs::Wayland => "WAYLAND_DISPLAY",
        Needs::X11 => "DISPLAY",
    };
    std::env::var(var).map(|v| !v.is_empty()).unwrap_or(false)
}

/// Whether the error is "no such binary", which means try the next tool rather
/// than give up: a host with `xclip` and no `xsel` is the ordinary case.
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

/// Walks the tools, running the first that is installed and whose display
/// server this session has. `text` is what goes down the tool's stdin on a
/// copy; a paste passes `None` and reads its stdout back.
async fn run(text: Option<&str>, pick: fn(&Tool) -> &'static [&'static str]) -> Result<String> {
    let mut last: Option<anyhow::Error> = None;
    for tool in TOOLS {
        if !have(&tool.needs) {
            continue;
        }
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
    let Some(t) = text else { return paste(argv).await };

    // A copy tool keeps *serving* the selection after it has read it: every one
    // of these forks a holder and lets the parent exit. So the output is never
    // collected - the fork inherits the pipes and holds them open for as long as
    // it owns the clipboard, which is a `wait_with_output` that waits forever
    // (this is exactly how a working `wl-copy` reported a timeout). Waiting on
    // the parent is the whole of the handshake, and the price is that a failure
    // is an exit status rather than a sentence.
    let mut child = Command::new(argv[0])
        .args(&argv[1..])
        .stdin(Stdio::piped())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
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

/// Reading is the ordinary shape: nothing forks, so the output can be collected
/// and a failure says why.
async fn paste(argv: &[&str]) -> Result<String> {
    let out = Command::new(argv[0])
        .args(&argv[1..])
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
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
