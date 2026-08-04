use std::process::Stdio;

use anyhow::{bail, Context, Result};
use tokio::process::{Child, Command};

/// Pinned to a private server socket, so it never collides with the user's tmux.
#[derive(Clone)]
pub struct Tmux {
    socket: String,
    history_limit: u32,
}

pub struct Screen {
    pub lines: Vec<String>,
    pub cx: u16,
    pub cy: u16,
    /// What the app last called itself. tmux parses OSC 0/2 for its own status line and the
    /// server outlives us, so this is the one piece of a pane's state that survives our
    /// restart without the app being asked to say it again.
    pub title: String,
    /// Whether the pane is on the alternate screen. tmux tracks this and we read it back so
    /// the emulator seed can match the mode the app was in, without which the `alt_screen`
    /// flag stays false after a daemon restart and wheel events go to history scrollback
    /// instead of the app.
    pub alt_screen: bool,
}

impl Tmux {
    pub fn new(socket: impl Into<String>, history_limit: u32) -> Self {
        Self {
            socket: socket.into(),
            history_limit,
        }
    }

    async fn run(&self, args: &[&str]) -> Result<String> {
        let started = std::time::Instant::now();
        let out = Command::new("tmux")
            .arg("-L")
            .arg(&self.socket)
            .args(args)
            .output()
            .await?;
        crate::perf::PERF.spawn.add(started.elapsed());
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

    /// `list-sessions` exits non-zero both when the server is down and when it is up
    /// with nothing in it; only the first says so on stderr.
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

    /// Whichever tmux command first needs a server forks it, and the server inherits that
    /// client's cgroup - `slopd.service`, so `make install-daemon` SIGTERMs every agent. A
    /// unit of its own is what keeps them.
    ///
    /// `Type=forking` and not a scope: `tmux start-server` daemonises, so the process
    /// systemd-run put in a scope forks the server and exits, systemd tears the empty scope
    /// down, and the next tmux command forks a server back into `slopd.service` - while
    /// systemd-run exits zero throughout. Hence checking the socket rather than the exit code;
    /// a host without systemd falls through to starting the server inline.
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

        // systemd-run returns once the job is queued, so the socket is what says the
        // server is up.
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

        // Panes inherit this at creation, so it has to be in place before the first
        // session is spawned.
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
        // Never let `new-session` be the command that forks the server: it would land in
        // slopd's own cgroup.
        self.ensure_server().await;

        // Again here rather than only in ensure_server, which is a no-op against a server
        // someone else already started.
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

        // Without this a detached pane clamps to the size of any later client. `window-size`
        // is a window option, so it wants a window target and the colon - see `resize`.
        let target = format!("{name}:");
        self.run(&["set-option", "-w", "-t", &target, "window-size", "manual"])
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

    pub async fn rename(&self, old: &str, new: &str) -> Result<()> {
        self.run(&["rename-session", "-t", old, new]).await?;
        Ok(())
    }

    /// `-e` keeps SGR escapes. The scrollback above the visible pane is the only way history
    /// survives a daemon restart: tmux kept it and our emulator did not.
    pub async fn capture(&self, name: &str, history: u32) -> Result<Screen> {
        let target = format!("{name}:.0");
        let start = format!("-{history}");
        let mut args: Vec<&str> = vec!["capture-pane", "-p", "-e", "-t", &target];
        if history > 0 {
            args.extend(["-S", start.as_str()]);
        }
        let body = self.run(&args).await?;
        // The title rides on the same question rather than a second one: a title with spaces
        // in it is why the tail is taken whole instead of split like the two figures ahead of
        // it, and it goes last for that reason.
        let pos = self
            .run(&[
                "display-message",
                "-p",
                "-t",
                &target,
                "#{cursor_x} #{cursor_y} #{alternate_on} #{pane_title}",
            ])
            .await
            .unwrap_or_default();

        let (cx, cy, alt_screen, title) = parse_pos(&pos);

        // `capture-pane` terminates its last row with a newline, so splitting on one coins a
        // final empty line the pane does not have. The seed writes the lines and then places
        // the cursor at an absolute row, so that phantom line scrolls the content up under it
        // and the cursor comes back one row low.
        let body = body.strip_suffix('\n').unwrap_or(&body);

        Ok(Screen {
            lines: body.split('\n').map(str::to_string).collect(),
            cx,
            cy,
            title,
            alt_screen,
        })
    }

    /// A pty and not pipes: tmux detaches a control client whose stdio isn't a terminal, which
    /// would orphan every live session as "down". Only `isatty` matters. The child is handed
    /// back so the attach lives, and `kill_on_drop` ends it with the reader task.
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

    /// The colon in `name:` is load-bearing: this takes a *window* target, and tmux resolves
    /// one by window name before session name - every window here is called `bwrap`, so a bare
    /// `b` prefix-matched another session's `bwrap` and resized that instead.
    pub async fn resize(&self, name: &str, cols: u16, rows: u16) -> Result<()> {
        let target = format!("{name}:");
        let cols = cols.to_string();
        let rows = rows.to_string();
        self.run(&["resize-window", "-t", &target, "-x", &cols, "-y", &rows])
            .await?;
        Ok(())
    }

    /// A session that outlived the daemon is whatever size the last terminal window asked for,
    /// and tmux is the only one left who remembers.
    pub async fn size(&self, name: &str) -> Option<(u16, u16)> {
        // `name:` for the same reason `resize` needs it - see there.
        let target = format!("{name}:");
        let out = self
            .run(&[
                "display-message",
                "-p",
                "-t",
                &target,
                "#{window_width} #{window_height}",
            ])
            .await
            .ok()?;
        let mut it = out.split_whitespace();
        let cols = it.next()?.parse().ok()?;
        let rows = it.next()?.parse().ok()?;
        Some((cols, rows))
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

    /// `send-keys -H` is hex, so mouse reports and escape sequences reach the app
    /// verbatim.
    pub async fn send_bytes(&self, name: &str, bytes: &[u8]) -> Result<()> {
        let target = format!("{name}:.0");
        let hexes: Vec<String> = bytes.iter().map(|b| format!("{b:02x}")).collect();
        let mut args: Vec<&str> = vec!["send-keys", "-t", &target, "-H"];
        args.extend(hexes.iter().map(String::as_str));
        self.run(&args).await?;
        Ok(())
    }

    /// Anything of a size goes through a tmux *buffer* rather than `send-keys`. The client
    /// packs a command's whole argv into one imsg to the server, and `-H` takes one byte per
    /// argument, so a hex write hits tmux's own "command too long" at **996 bytes** - which
    /// is an ordinary paste, and it failed silently. A buffer is loaded from stdin, which has
    /// no such ceiling.
    ///
    /// `-r` because `paste-buffer` rewrites `\n` to `\r` otherwise, and the hex road this
    /// replaces sent the bytes as they came. Bracketing markers are not added here either:
    /// sending them caused literal `^[[200~` display in applications like Claude Code/Ink
    /// that don't strip them. The raw text is what the child process expects.
    ///
    /// The buffer is named after the session so two panes pasting at once cannot take each
    /// other's, and `-d` drops it rather than leaving it on the user's buffer stack.
    pub async fn paste_bytes(&self, name: &str, bytes: &[u8]) -> Result<()> {
        use tokio::io::AsyncWriteExt;

        let buf = format!("slopworld-{name}");
        let mut child = Command::new("tmux")
            .arg("-L")
            .arg(&self.socket)
            .args(["load-buffer", "-b", buf.as_str(), "-"])
            .stdin(Stdio::piped())
            .stdout(Stdio::null())
            .stderr(Stdio::piped())
            .spawn()?;

        // Closed before the wait: tmux reads this to EOF, so holding it open is a hang
        // rather than a slow paste.
        {
            let mut stdin = child.stdin.take().context("load-buffer stdin")?;
            stdin.write_all(bytes).await?;
            stdin.shutdown().await?;
        }
        let out = child.wait_with_output().await?;
        if !out.status.success() {
            bail!(
                "tmux load-buffer: {}",
                String::from_utf8_lossy(&out.stderr).trim()
            );
        }

        let target = format!("{name}:.0");
        self.run(&["paste-buffer", "-d", "-r", "-b", &buf, "-t", &target])
            .await?;
        Ok(())
    }
}

/// `display-message`'s one line: two figures, a flag, and the title as the whole tail.
/// Anything missing reads as the boot answer rather than as a failure - the pane is asked
/// about with `unwrap_or_default()`, so an empty line is what a dead session hands back.
fn parse_pos(pos: &str) -> (u16, u16, bool, String) {
    let mut it = pos.splitn(4, ' ');
    let cx = it.next().and_then(|v| v.parse().ok()).unwrap_or(0);
    let cy = it
        .next()
        .and_then(|v| v.trim_end().parse().ok())
        .unwrap_or(0);
    // A tmux flag is `1` or `0`, which is not what `bool::from_str` reads - it takes `true`
    // and `false` and nothing else, so parsing this would answer `false` on either flag.
    let alt = it.next().map(|v| v.trim_end() == "1").unwrap_or(false);
    (cx, cy, alt, clean_title(it.next().unwrap_or("")))
}

/// The seed states this back to a fresh emulator as an OSC, so a control character in it
/// would be an escape sequence somebody else's app got to write. Stripped rather than
/// refused: a title is decoration, and the answer to an odd one is a plain one.
fn clean_title(s: &str) -> String {
    s.chars().filter(|c| !c.is_control()).take(512).collect()
}

#[cfg(test)]
mod tests {
    use super::{clean_title, parse_pos};

    #[test]
    fn a_title_cannot_carry_an_escape() {
        // display-message ends its answer with one, and the title is the tail of that line.
        assert_eq!(clean_title("Add status labels\n"), "Add status labels");
        assert_eq!(clean_title("\x1b]0;other\x07here"), "]0;otherhere");
        assert_eq!(clean_title(""), "");
    }

    #[test]
    fn the_alternate_screen_flag_is_a_digit_and_not_a_word() {
        assert_eq!(parse_pos("12 3 1 vim\n"), (12, 3, true, "vim".into()));
        assert_eq!(parse_pos("12 3 0 vim\n"), (12, 3, false, "vim".into()));
        // A title has spaces in it and the flag is read from the field ahead of it.
        assert_eq!(
            parse_pos("0 0 1 fix the thing\n"),
            (0, 0, true, "fix the thing".into())
        );
        // No answer at all: the pane is gone, and none of this is worth failing over.
        assert_eq!(parse_pos(""), (0, 0, false, String::new()));
    }
}
