use anyhow::{bail, Result};
use tokio::process::Command;

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

    pub async fn size(&self, name: &str) -> Result<(u16, u16)> {
        let target = format!("{name}:.0");
        let out = self
            .run(&[
                "display-message",
                "-p",
                "-t",
                &target,
                "#{pane_width} #{pane_height}",
            ])
            .await?;
        let mut it = out.split_whitespace();
        let w = it.next().and_then(|v| v.parse().ok()).unwrap_or(80);
        let h = it.next().and_then(|v| v.parse().ok()).unwrap_or(24);
        Ok((w, h))
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
}
