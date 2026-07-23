use std::process::Stdio;

use anyhow::{bail, Result};
use tokio::process::{Child, Command};

/// Thin async wrapper over the tmux CLI, pinned to a private server socket.
#[derive(Clone)]
pub struct Tmux {
    socket: String,
}

pub struct Screen {
    pub lines: Vec<String>,
    pub cx: u16,
    pub cy: u16,
}

impl Tmux {
    pub fn new(socket: impl Into<String>) -> Self {
        Self {
            socket: socket.into(),
        }
    }

    async fn run(&self, args: &[&str]) -> Result<String> {
        let out = Command::new("tmux")
            .arg("-L")
            .arg(&self.socket)
            .args(args)
            .output()
            .await?;
        if !out.status.success() {
            bail!(
                "tmux {}: {}",
                args.join(" "),
                String::from_utf8_lossy(&out.stderr).trim()
            );
        }
        Ok(String::from_utf8_lossy(&out.stdout).into_owned())
    }

    /// tmux exits non-zero when the server isn't running yet, which is not an error.
    pub async fn list(&self) -> Vec<String> {
        match self.run(&["list-sessions", "-F", "#{session_name}"]).await {
            Ok(s) => s.lines().map(str::to_string).collect(),
            Err(_) => Vec::new(),
        }
    }

    pub async fn exists(&self, name: &str) -> bool {
        self.list().await.iter().any(|s| s == name)
    }

    pub async fn spawn(
        &self,
        name: &str,
        dir: &str,
        cols: u16,
        rows: u16,
        argv: &[String],
    ) -> Result<()> {
        let cols = cols.to_string();
        let rows = rows.to_string();
        let mut args: Vec<&str> = vec![
            "new-session", "-d", "-s", name, "-x", &cols, "-y", &rows, "-c", dir,
        ];
        args.push("--");
        args.extend(argv.iter().map(String::as_str));
        self.run(&args).await?;

        // Without this a detached pane clamps to the size of any later client.
        self.run(&["set-option", "-t", name, "window-size", "manual"])
            .await
            .ok();
        self.run(&["set-option", "-t", name, "status", "off"])
            .await
            .ok();
        Ok(())
    }

    pub async fn kill(&self, name: &str) -> Result<()> {
        self.run(&["kill-session", "-t", name]).await?;
        Ok(())
    }

    /// `-e` keeps SGR escapes, so tmux stays the terminal emulator and the mod
    /// only ever has to parse colour runs.
    pub async fn capture(&self, name: &str) -> Result<Screen> {
        let target = format!("{name}:.0");
        let body = self.run(&["capture-pane", "-p", "-e", "-t", &target]).await?;
        let pos = self
            .run(&[
                "display-message",
                "-p",
                "-t",
                &target,
                "#{cursor_x} #{cursor_y}",
            ])
            .await
            .unwrap_or_default();

        let mut it = pos.split_whitespace();
        let cx = it.next().and_then(|v| v.parse().ok()).unwrap_or(0);
        let cy = it.next().and_then(|v| v.parse().ok()).unwrap_or(0);

        Ok(Screen {
            lines: body.split('\n').map(str::to_string).collect(),
            cx,
            cy,
        })
    }

    /// Spawns a control-mode client attached to one session: it writes
    /// `%`-prefixed notifications (incl. `%output`) to its stdout. Returns the
    /// child (held by the caller so the attach lives; `kill_on_drop` tears it down
    /// when the reader task ends) plus the pty master to read those notifications.
    ///
    /// The client runs on a pty, not pipes: tmux 3.7 immediately detaches a
    /// control client whose stdio isn't a terminal (it emits `%exit` right after
    /// `%session-changed`), which would orphan every live session as "dead". Only
    /// `isatty` matters here - no controlling terminal is needed - so we hand tmux
    /// a pty slave and read the master. Commands still go out over separate `tmux`
    /// invocations, so the master is read-only for us.
    pub fn control_attach(&self, name: &str, cols: u16, rows: u16) -> Result<(Child, std::fs::File)> {
        use nix::pty::{openpty, Winsize};

        let ws = Winsize {
            ws_row: rows,
            ws_col: cols,
            ws_xpixel: 0,
            ws_ypixel: 0,
        };
        let pty = openpty(Some(&ws), None)?;

        let child = Command::new("tmux")
            .arg("-L")
            .arg(&self.socket)
            .args(["-C", "attach", "-t", name])
            .stdin(Stdio::from(pty.slave.try_clone()?))
            .stdout(Stdio::from(pty.slave.try_clone()?))
            .stderr(Stdio::from(pty.slave))
            .kill_on_drop(true)
            .spawn()?;

        Ok((child, std::fs::File::from(pty.master)))
    }

    pub async fn resize(&self, name: &str, cols: u16, rows: u16) -> Result<()> {
        let cols = cols.to_string();
        let rows = rows.to_string();
        self.run(&["resize-window", "-t", name, "-x", &cols, "-y", &rows])
            .await?;
        Ok(())
    }

    /// `keys` are tmux key names (Enter, C-c, Up) or literal text when `literal`.
    pub async fn send_keys(&self, name: &str, keys: &[String], literal: bool) -> Result<()> {
        let target = format!("{name}:.0");
        let mut args: Vec<&str> = vec!["send-keys", "-t", &target];
        if literal {
            args.push("-l");
        }
        args.extend(keys.iter().map(String::as_str));
        self.run(&args).await?;
        Ok(())
    }

    /// Writes raw bytes into the pane via `send-keys -H` (hex), so arbitrary
    /// control bytes (mouse reports, escape sequences) reach the app verbatim.
    pub async fn send_bytes(&self, name: &str, bytes: &[u8]) -> Result<()> {
        let target = format!("{name}:.0");
        let hexes: Vec<String> = bytes.iter().map(|b| format!("{b:02x}")).collect();
        let mut args: Vec<&str> = vec!["send-keys", "-t", &target, "-H"];
        args.extend(hexes.iter().map(String::as_str));
        self.run(&args).await?;
        Ok(())
    }
}
