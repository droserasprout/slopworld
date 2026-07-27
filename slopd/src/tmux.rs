use std::process::Stdio;

use anyhow::{bail, Result};
use tokio::process::{Child, Command};

/// Thin async wrapper over the tmux CLI, pinned to a private server socket.
#[derive(Clone)]
pub struct Tmux {
    socket: String,
    /// Scrollback tmux keeps per pane. Read once at startup, like the socket.
    history_limit: u32,
}

pub struct Screen {
    pub lines: Vec<String>,
    pub cx: u16,
    pub cy: u16,
}

impl Tmux {
    pub fn new(socket: impl Into<String>, history_limit: u32) -> Self {
        Self {
            socket: socket.into(),
            history_limit,
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

    /// `list-sessions` exits non-zero both when the server is down and when it is
    /// up with nothing in it; only the first says so on stderr.
    async fn server_running(&self) -> bool {
        match Command::new("tmux")
            .arg("-L")
            .arg(&self.socket)
            .arg("list-sessions")
            .output()
            .await
        {
            Ok(o) => {
                o.status.success()
                    || !String::from_utf8_lossy(&o.stderr).contains("no server running")
            }
            Err(_) => false,
        }
    }

    /// Starts the tmux server, if it isn't up, in a transient systemd unit of its
    /// own.
    ///
    /// Whichever tmux command first needs a server is the one that forks it, and
    /// the server inherits that client's cgroup. Started from slopd, that is
    /// `slopd.service` - so `systemctl --user restart slopd`, which is exactly
    /// what `make install-daemon` runs, SIGTERMs the server and every agent
    /// under it. Putting the server in `slopworld-tmux.service` takes it out of
    /// the unit's cgroup, and the agents then sit through a redeploy untouched.
    ///
    /// It is a *service* with `Type=forking` and not a scope, which is what this
    /// tried first and what quietly did the opposite. `tmux start-server`
    /// daemonises: the process systemd-run put in the scope forks the server and
    /// exits, systemd sees the scope's own process gone and tears the scope
    /// down - taking the fork with it, same cgroup. The next tmux command then
    /// forked its own server, and *that* one sat in `slopd.service` after all.
    /// It looked like it worked, because `systemd-run` had exited zero.
    /// `Type=forking` is the shape that fits a program which daemonises: systemd
    /// waits for the parent to exit and adopts what is left in the cgroup.
    ///
    /// So the log now says what happened rather than what was attempted: the
    /// socket is checked afterwards, and a server that did not come up that way
    /// is started inline. Hosts without systemd end up there too: same behaviour
    /// as before any of this existed, which is to say a redeploy there still
    /// costs the agents. `KillMode=process` in `slopd.service` is the other
    /// half, for the server this did not get to start.
    pub async fn ensure_server(&self) {
        if self.server_running().await {
            return;
        }

        let unit = Command::new("systemd-run")
            .args([
                "--user",
                "--quiet",
                "--collect",
                "--unit=slopworld-tmux",
                "--property=Type=forking",
                "--",
                "tmux",
                "-L",
                &self.socket,
                "start-server",
            ])
            .status()
            .await;

        // systemd-run returns once the job is queued, so the socket is what says
        // the server is up - the same question every other caller asks.
        let mut up = false;
        if matches!(&unit, Ok(s) if s.success()) {
            for _ in 0..20 {
                if self.server_running().await {
                    up = true;
                    break;
                }
                tokio::time::sleep(std::time::Duration::from_millis(100)).await;
            }
        }

        if up {
            tracing::info!("tmux server started in slopworld-tmux.service");
        } else {
            match unit {
                Ok(s) if s.success() => {
                    tracing::warn!("slopworld-tmux.service started no server; starting tmux inline")
                }
                Ok(s) => tracing::warn!("systemd-run failed ({s}); starting tmux inline"),
                Err(_) => tracing::warn!("systemd-run not available; starting tmux inline"),
            }
            if let Err(e) = self.run(&["start-server"]).await {
                tracing::error!("start tmux server: {e:#}");
                return;
            }
        }

        // Panes inherit this at creation, so it has to be in place before the
        // first session is spawned.
        let limit = self.history_limit.to_string();
        self.run(&["set-option", "-g", "history-limit", &limit])
            .await
            .ok();
    }

    pub async fn spawn(
        &self,
        name: &str,
        dir: &str,
        cols: u16,
        rows: u16,
        argv: &[String],
    ) -> Result<()> {
        // Never let `new-session` be the command that forks the server: it would
        // land in slopd's own cgroup. See ensure_server.
        self.ensure_server().await;

        // A pane takes its scrollback size at creation, so this has to be set
        // before new-session - and again here rather than only in ensure_server,
        // which is a no-op against a server someone else already started.
        let limit = self.history_limit.to_string();
        self.run(&["set-option", "-g", "history-limit", &limit])
            .await
            .ok();

        let cols = cols.to_string();
        let rows = rows.to_string();
        let mut args: Vec<&str> = vec![
            "new-session",
            "-d",
            "-s",
            name,
            "-x",
            &cols,
            "-y",
            &rows,
            "-c",
            dir,
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

    /// The agent keeps running: only the handle it answers to changes.
    pub async fn rename(&self, old: &str, new: &str) -> Result<()> {
        self.run(&["rename-session", "-t", old, new]).await?;
        Ok(())
    }

    /// `-e` keeps SGR escapes, so tmux stays the terminal emulator and the mod
    /// only ever has to parse colour runs.
    ///
    /// `history` lines of scrollback are included above the visible pane; feeding
    /// those into a fresh emulator is the only way scrollback survives a daemon
    /// restart, because tmux kept it and our emulator did not.
    pub async fn capture(&self, name: &str, history: u32) -> Result<Screen> {
        let target = format!("{name}:.0");
        let start = format!("-{history}");
        let mut args: Vec<&str> = vec!["capture-pane", "-p", "-e", "-t", &target];
        if history > 0 {
            args.extend(["-S", start.as_str()]);
        }
        let body = self.run(&args).await?;
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
    /// The client runs on a pty, not pipes: tmux immediately detaches a control
    /// client whose stdio isn't a terminal, which would orphan every live session
    /// as "down". Only `isatty` matters here - no controlling terminal is needed -
    /// so we hand tmux a pty slave and read the master. Commands still go out over
    /// separate `tmux` invocations, so the master is read-only for us.
    pub fn control_attach(
        &self,
        name: &str,
        cols: u16,
        rows: u16,
    ) -> Result<(Child, std::fs::File)> {
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

    /// Bounces the window one column narrower and back, which makes tmux send the
    /// pane a SIGWINCH.
    ///
    /// A reattached session is seeded from a text capture, so the emulator knows
    /// the characters but none of the modes the app had set - alternate screen,
    /// mouse reporting, cursor shape, bracketed paste - and nothing puts those
    /// back until the app next redraws in full. A resize is the one event every
    /// TUI answers with exactly that.
    pub async fn nudge_redraw(&self, name: &str, cols: u16, rows: u16) -> Result<()> {
        if cols < 2 {
            return Ok(());
        }
        self.resize(name, cols - 1, rows).await?;
        tokio::time::sleep(std::time::Duration::from_millis(60)).await;
        self.resize(name, cols, rows).await
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
