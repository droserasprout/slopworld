//! tmux session transport and recovery metadata. `server` owns startup and readiness.

pub(crate) mod control;
mod server;

use std::collections::HashMap;
use std::process::Stdio;
use std::sync::OnceLock;
#[cfg(test)]
use std::sync::{Arc, Mutex};

use anyhow::{Context, Result, bail};
use tokio::process::{Child, Command};

use crate::session::State;

/// History retained by tmux and requested when attaching a terminal reader.
pub const SCROLLBACK_LINES: u32 = 10_000;

/// Private socket name, cached for the daemon lifetime.
/// `SLOPD_TMUX_SOCKET` overrides `slopworld` for isolated daemon instances.
pub fn tmux_socket() -> &'static str {
    static SOCKET: OnceLock<String> = OnceLock::new();
    SOCKET.get_or_init(|| {
        std::env::var("SLOPD_TMUX_SOCKET")
            .ok()
            .map(|s| s.trim().to_string())
            .filter(|s| !s.is_empty())
            .unwrap_or_else(|| "slopworld".to_string())
    })
}

const ACTIVITY_STATE: &str = "@slopworld_state";
const ACTIVITY_SINCE: &str = "@slopworld_state_since";
const HOST_PROJECT: &str = "@slopworld_host_project";
const HOST_PATH: &str = "@slopworld_host_path";
const WORKER: &str = "@slopworld_worker";
const WORKER_PARENT: &str = "@slopworld_worker_parent";
const WORKER_TASK: &str = "@slopworld_worker_task";
const WORKER_DURABLE: &str = "@slopworld_worker_durable";
const WORKER_STATE: &str = "@slopworld_worker_state";
const READER_METADATA: &str = "@slopworld_reader";
// tmux otherwise counts newer emoji modifiers as separate wide characters (for example
// 🤝🏻 occupies four cells and 🫱🏻‍🫲🏼 six). Its emoji grapheme handling collapses the
// complete sequence to two cells when modifiers have zero width.
const EMOJI_MODIFIER_WIDTHS: &str = "U+1F3FB-U+1F3FF=0";

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct ReaderMetadata {
    pub intent: String,
    pub label: String,
    #[serde(default)]
    pub original_label: String,
    pub project: String,
    pub worktree: String,
    pub path: String,
    pub key: String,
    pub scope: String,
    pub pinned: bool,
    pub line: u32,
}

#[cfg(test)]
#[derive(Default)]
struct RenameTestControls {
    calls: usize,
    fail_call: usize,
    pause: Option<(Arc<tokio::sync::Notify>, Arc<tokio::sync::Notify>)>,
}

/// Use a private server socket to keep daemon sessions separate from the user's tmux sessions.
#[derive(Clone)]
pub struct Tmux {
    socket: String,
    #[cfg(test)]
    rename_test: Arc<Mutex<RenameTestControls>>,
}

#[derive(Debug, Clone)]
pub struct WorkerMetadata {
    pub project: Option<String>,
    pub worktree: Option<String>,
    pub parent: String,
    pub task_id: String,
    pub durable: bool,
    pub state_id: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct HostMetadata {
    pub path: Option<String>,
    pub command: Option<String>,
}

pub struct Screen {
    pub bracketed_paste: bool,
    pub lines: Vec<String>,
    pub cx: u16,
    pub cy: u16,
    /// The latest application title. tmux parses OSC 0/2 for its status line.
    /// The tmux server preserves the title after a daemon restart without another title update from the application.
    pub title: String,
    /// Whether the pane uses the alternate screen.
    /// Read this state from tmux to restore the emulator mode after a daemon restart.
    /// Otherwise, `alt_screen` remains false and wheel events scroll history instead of reaching the application.
    pub alt_screen: bool,
}

impl Tmux {
    pub fn new(socket: impl Into<String>) -> Self {
        Self {
            socket: socket.into(),
            #[cfg(test)]
            rename_test: Arc::default(),
        }
    }

    fn socket_args(&self) -> [&str; 2] {
        // Test fixtures supply absolute socket paths instead of using the host's
        // default socket directory. Production keeps its named socket contract.
        #[cfg(test)]
        if std::path::Path::new(&self.socket).is_absolute() {
            return ["-S", &self.socket];
        }
        ["-L", &self.socket]
    }

    async fn run(&self, args: &[&str]) -> Result<String> {
        let out = Command::new("tmux")
            .args(self.socket_args())
            .args(args)
            .output()
            .await?;
        if !out.status.success() {
            bail!(
                "tmux command failed: {}",
                crate::sandbox::sanitize_diagnostic(&String::from_utf8_lossy(&out.stderr))
            );
        }
        Ok(String::from_utf8_lossy(&out.stdout).into_owned())
    }

    /// tmux exits non-zero when the server isn't running yet, which is not an error.
    pub async fn list(&self) -> Vec<String> {
        self.list_checked().await.unwrap_or_default()
    }

    pub(crate) async fn list_checked(&self) -> Result<Vec<String>> {
        let output = Command::new("tmux")
            .args(self.socket_args())
            .args(["list-sessions", "-F", "#{session_name}"])
            .output()
            .await?;
        // A running server with no sessions confirms that a departed host pane
        // is absent. Other failures must not be used as evidence of process exit.
        if !output.status.success() {
            let error = String::from_utf8_lossy(&output.stderr);
            if error.trim() == "no sessions" {
                return Ok(Vec::new());
            }
            bail!(
                "tmux command failed: {}",
                crate::sandbox::sanitize_diagnostic(&error)
            );
        }
        Ok(String::from_utf8_lossy(&output.stdout)
            .lines()
            .map(str::to_string)
            .collect())
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
        host: bool,
    ) -> Result<()> {
        // Never let `new-session` be the command that forks the server: it would land in
        // slopd's own cgroup.
        self.ensure_server().await?;

        // Again here rather than only in ensure_server, which is a no-op against a server
        // someone else already started.
        let limit = crate::tmux::SCROLLBACK_LINES.to_string();
        drop(
            self.run(&["set-option", "-g", "history-limit", &limit])
                .await,
        );

        let cols = cols.to_string();
        let rows = rows.to_string();
        let mut args: Vec<&str> = vec![
            "new-session",
            "-P",
            "-F",
            "#{session_id}",
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
        let created = self.run(&args).await?;
        let created = created.trim();
        if !created
            .strip_prefix('$')
            .is_some_and(|id| !id.is_empty() && id.bytes().all(|byte| byte.is_ascii_digit()))
        {
            bail!("tmux did not return the created session identity");
        }

        // Without this a detached pane clamps to the size of any later client. `window-size`
        // is a window option, so it wants a window target and the colon - see `resize`.
        let target = format!("{created}:");
        drop(
            self.run(&["set-option", "-w", "-t", &target, "window-size", "manual"])
                .await,
        );
        drop(
            self.run(&["set-option", "-t", created, "status", "off"])
                .await,
        );
        if let Err(error) = self
            .run(&[
                "set-option",
                "-t",
                created,
                "@slopworld_host",
                if host { "1" } else { "0" },
            ])
            .await
        {
            // Session IDs cannot follow a name reused while this command was in flight.
            if let Err(cleanup) = self.kill(created).await {
                return Err(error.context(format!(
                    "created tmux session {created}; rollback failed: {cleanup:#}"
                )));
            }
            return Err(error);
        }
        Ok(())
    }

    /// Replace the silent startup process only after the control reader is ready. The pane
    /// identity and negotiated size survive, so its first output reaches the same reader.
    pub async fn start_command(&self, name: &str, dir: &str, argv: &[String]) -> Result<()> {
        let target = format!("{name}:.0");
        let mut args = vec!["respawn-pane", "-k", "-t", &target, "-c", dir, "--"];
        args.extend(argv.iter().map(String::as_str));
        self.run(&args).await?;
        Ok(())
    }

    /// Keep process status and terminal output until the reader records the exit.
    /// Detaching readers on pane death wakes their control streams without a timer.
    pub(crate) async fn retain_exit(&self, name: &str) -> Result<()> {
        let target = format!("{name}:.0");
        self.run(&["set-option", "-p", "-t", &target, "remain-on-exit", "on"])
            .await?;
        // Session IDs survive rename; a saved name would leave the renamed
        // pane's readers attached after process exit.
        let id = self
            .run(&["display-message", "-p", "-t", &target, "#{session_id}"])
            .await?;
        let hook = format!("detach-client -s {}", id.trim());
        self.run(&["set-hook", "-t", name, "pane-died", &hook])
            .await?;
        Ok(())
    }

    /// A live pane is distinct from a disconnected capture client.
    pub(crate) async fn pane_exit(&self, name: &str) -> Result<Option<String>> {
        let target = format!("{name}:.0");
        let value = self
            .run(&[
                "display-message",
                "-p",
                "-t",
                &target,
                "#{pane_dead}|#{pane_dead_status}|#{pane_dead_signal}",
            ])
            .await?;
        let mut fields = value.trim().split('|');
        match fields.next() {
            Some("0") => return Ok(None),
            Some("1") => {}
            // display-message can succeed with empty fields for a missing target.
            // Exit handling confirms absence with a checked session listing.
            _ => bail!("tmux returned no pane status for {name}"),
        }
        let status = fields.next().unwrap_or("");
        let signal = fields.next().unwrap_or("");
        Ok(Some(format!("exit status {status}, signal {signal}")))
    }

    /// The tmux server can continue after slopd stops.
    /// Keep the host flag in a private session option to distinguish host shells from agents during adoption.
    pub async fn is_host(&self, name: &str) -> bool {
        self.run(&["show-options", "-qv", "-t", name, "@slopworld_host"])
            .await
            .is_ok_and(|value| value.trim() == "1")
    }

    /// Store each host tab's project and last known directory on the tmux server.
    /// The configuration copy supports recovery after a machine restart.
    /// These tmux options take precedence while the server continues across daemon deployment.
    /// Adoption uses them to restore shell directory changes.
    pub async fn set_host_metadata(&self, name: &str, project: &str, path: &str) -> Result<()> {
        self.run(&["set-option", "-t", name, HOST_PROJECT, project])
            .await?;
        self.run(&["set-option", "-t", name, HOST_PATH, path])
            .await?;
        Ok(())
    }

    pub async fn host_metadata(&self, name: &str) -> Option<(String, String)> {
        let project = self.option(name, HOST_PROJECT).await?;
        let path = self.option(name, HOST_PATH).await?;
        Some((project, path))
    }

    /// Reader identity belongs to the tmux session so it survives a daemon redeploy.
    pub async fn set_reader_metadata(&self, name: &str, value: &ReaderMetadata) -> Result<()> {
        let encoded = serde_json::to_string(value)?;
        self.run(&["set-option", "-t", name, READER_METADATA, &encoded])
            .await?;
        Ok(())
    }

    pub async fn reader_metadata(&self, name: &str) -> Option<ReaderMetadata> {
        serde_json::from_str(&self.option(name, READER_METADATA).await?).ok()
    }

    /// Task-owned worker identity survives a daemon redeploy with the tmux session.
    pub async fn set_worker_metadata(
        &self,
        name: &str,
        parent: &str,
        task_id: &str,
        durable: bool,
        state_id: &str,
    ) -> Result<()> {
        self.run(&["set-option", "-t", name, WORKER, "1"]).await?;
        self.run(&["set-option", "-t", name, WORKER_PARENT, parent])
            .await?;
        self.run(&["set-option", "-t", name, WORKER_TASK, task_id])
            .await?;
        self.run(&[
            "set-option",
            "-t",
            name,
            WORKER_DURABLE,
            if durable { "1" } else { "0" },
        ])
        .await?;
        self.run(&["set-option", "-t", name, WORKER_STATE, state_id])
            .await?;
        Ok(())
    }

    pub async fn set_worker_worktree(
        &self,
        name: &str,
        project: &str,
        worktree: &str,
    ) -> Result<()> {
        self.run(&[
            "set-option",
            "-t",
            name,
            "@slopworld-worker-project",
            project,
        ])
        .await?;
        self.run(&[
            "set-option",
            "-t",
            name,
            "@slopworld-worker-worktree",
            worktree,
        ])
        .await?;
        Ok(())
    }

    pub async fn worker_metadata(&self, name: &str) -> Option<WorkerMetadata> {
        if self.option(name, WORKER).await?.trim() != "1" {
            return None;
        }
        Some(WorkerMetadata {
            project: self.option(name, "@slopworld-worker-project").await,
            worktree: match self.option(name, "@slopworld-worker-worktree").await {
                Some(value) => Some(value),
                None => self.option(name, "@slopworld-worker-workspace").await,
            },
            parent: self.option(name, WORKER_PARENT).await?,
            task_id: self.option(name, WORKER_TASK).await?,
            durable: self
                .option(name, WORKER_DURABLE)
                .await
                .is_some_and(|value| value.trim() == "1"),
            state_id: self.option(name, WORKER_STATE).await,
        })
    }

    /// The process tmux created for pane zero. A missing pane is a live-state observation, not a
    /// successful launch, so callers keep the saved plan and report the observation as unknown.
    pub async fn pane_pid(&self, name: &str) -> Option<u32> {
        self.run(&[
            "display-message",
            "-p",
            "-t",
            &format!("{name}:.0"),
            "#{pane_pid}",
        ])
        .await
        .ok()
        .and_then(|value| value.trim().parse().ok())
    }

    /// Batch the same panes targeted by `name:.0`: pane zero of each session's current
    /// window. Other panes/windows must never overwrite the managed terminal's cwd.
    pub async fn current_host_metadata_all(&self) -> Result<HashMap<String, HostMetadata>> {
        let format = "#{n:session_name}:#{session_name}#{n:pane_current_path}:#{pane_current_path}#{n:pane_current_command}:#{pane_current_command}";
        let output = self
            .run(&[
                "list-panes",
                "-a",
                "-f",
                "#{&&:#{window_active},#{==:#{pane_index},0}}",
                "-F",
                format,
            ])
            .await?;
        parse_host_metadata_rows(&output)
            .ok_or_else(|| anyhow::anyhow!("invalid tmux host metadata framing"))
    }

    pub async fn current_path(&self, name: &str) -> Option<String> {
        let target = format!("{name}:.0");
        // tmux cannot always inspect the cwd of a sandboxed pane whose immediate
        // process is pasta/bwrap. The launch directory is still useful for links
        // printed by agents started at the project root.
        for format in ["#{pane_current_path}", "#{pane_start_path}"] {
            if let Some(path) = self
                .run(&["display-message", "-p", "-t", &target, format])
                .await
                .ok()
                .map(|path| path.trim().to_string())
                .filter(|path| !path.is_empty())
            {
                return Some(path);
            }
        }
        None
    }

    pub(crate) async fn set_option(&self, name: &str, option: &str, value: &str) -> Result<()> {
        self.run(&["set-option", "-t", name, option, value]).await?;
        Ok(())
    }

    pub(crate) async fn option(&self, name: &str, option: &str) -> Option<String> {
        self.run(&["show-options", "-qv", "-t", name, option])
            .await
            .ok()
            .map(|value| value.trim().to_string())
            .filter(|value| !value.is_empty())
    }

    /// State ages live on the tmux server as well as in slopd's fallback file cache. The
    /// server is the durable owner for a pane that survives a daemon redeploy, just like the
    /// host marker and the pane title.
    pub async fn activity(&self, name: &str) -> Option<(State, u64)> {
        let state = self
            .run(&["show-options", "-qv", "-t", name, ACTIVITY_STATE])
            .await
            .ok()?
            .trim()
            .to_string();
        let state = match state.as_str() {
            "working" => State::Working,
            "waiting" => State::Waiting,
            "idle" => State::Idle,
            _ => return None,
        };
        let since = self
            .run(&["show-options", "-qv", "-t", name, ACTIVITY_SINCE])
            .await
            .ok()?
            .trim()
            .parse()
            .ok()?;
        Some((state, since))
    }

    pub async fn set_activity(&self, name: &str, state: State, since: u64) -> Result<()> {
        let state = match state {
            State::Working => "working",
            State::Waiting => "waiting",
            State::Idle => "idle",
            State::Down => return self.clear_activity(name).await,
        };
        let since = since.to_string();
        self.run(&["set-option", "-t", name, ACTIVITY_STATE, state])
            .await?;
        self.run(&["set-option", "-t", name, ACTIVITY_SINCE, &since])
            .await?;
        Ok(())
    }

    pub async fn clear_activity(&self, name: &str) -> Result<()> {
        let state = self
            .run(&["set-option", "-uq", "-t", name, ACTIVITY_STATE])
            .await;
        let since = self
            .run(&["set-option", "-uq", "-t", name, ACTIVITY_SINCE])
            .await;
        match (state, since) {
            (Ok(_), Ok(_)) => Ok(()),
            (Err(error), Ok(_)) | (Ok(_), Err(error)) => Err(error),
            (Err(error), Err(second)) => {
                Err(error.context(format!("both activity removals failed: {second:#}")))
            }
        }
    }

    pub async fn kill(&self, name: &str) -> Result<()> {
        self.run(&["kill-session", "-t", name]).await?;
        Ok(())
    }

    pub async fn rename(&self, old: &str, new: &str) -> Result<()> {
        #[cfg(test)]
        {
            let mut controls = self.rename_test.lock().unwrap();
            controls.calls += 1;
            let call = controls.calls;
            if controls.fail_call == call {
                bail!("injected tmux rename failure on call {call}");
            }
        }
        self.run(&["rename-session", "-t", old, new]).await?;
        #[cfg(test)]
        {
            let pause = self.rename_test.lock().unwrap().pause.take();
            if let Some((started, release)) = pause {
                started.notify_one();
                release.notified().await;
            }
        }
        Ok(())
    }

    #[cfg(test)]
    pub(crate) fn fail_rename_call_for_test(&self, call: usize) {
        self.rename_test.lock().unwrap().fail_call = call;
    }

    #[cfg(test)]
    pub(crate) fn pause_after_rename_for_test(
        &self,
    ) -> (Arc<tokio::sync::Notify>, Arc<tokio::sync::Notify>) {
        let started = Arc::new(tokio::sync::Notify::new());
        let release = Arc::new(tokio::sync::Notify::new());
        self.rename_test.lock().unwrap().pause = Some((started.clone(), release.clone()));
        (started, release)
    }

    /// `-e` preserves SGR escape sequences.
    /// Capture tmux scrollback to restore history that the emulator loses during a daemon restart.
    pub async fn capture(&self, name: &str, history: u32) -> Result<Screen> {
        let target = format!("{name}:.0");
        let start = format!("-{history}");
        let mut args: Vec<&str> = vec!["capture-pane", "-p", "-e", "-t", &target];
        if history > 0 {
            args.extend(["-S", start.as_str()]);
        }
        let body = self.run(&args).await?;
        // Request the title with the cursor position and alternate-screen flag.
        // Put the title last so parsing preserves spaces within it.
        let pos = self
            .run(&[
                "display-message",
                "-p",
                "-t",
                &target,
                "#{bracket_paste_flag} #{cursor_x} #{cursor_y} #{alternate_on} #{pane_title}",
            ])
            .await
            .unwrap_or_default();

        let (paste, pos) = pos.split_once(' ').unwrap_or(("0", ""));
        let bracketed_paste = paste == "1";
        let (cx, cy, alt_screen, title) = parse_pos(pos);

        // Remove the final newline from `capture-pane` before splitting rows.
        // Otherwise, the extra empty row scrolls the restored content upward.
        // The cursor then appears one row too low because restoration uses an absolute cursor position.
        let body = body.strip_suffix('\n').unwrap_or(&body);

        Ok(Screen {
            bracketed_paste,
            lines: body.split('\n').map(str::to_string).collect(),
            cx,
            cy,
            title,
            alt_screen,
        })
    }

    /// Use a PTY so the control client's standard streams pass `isatty`.
    /// tmux detaches clients whose streams are not terminals, which would make live sessions appear stopped.
    /// Return the child to keep the connection active. `kill_on_drop` terminates it with the reader task.
    pub fn control_attach(
        &self,
        name: &str,
        cols: u16,
        rows: u16,
    ) -> Result<(Child, std::fs::File)> {
        use nix::pty::{Winsize, openpty};

        let ws = Winsize {
            ws_row: rows,
            ws_col: cols,
            ws_xpixel: 0,
            ws_ypixel: 0,
        };
        let pty = openpty(Some(&ws), None)?;

        let child = Command::new("tmux")
            .args(self.socket_args())
            .args(["-C", "attach", "-t", name])
            .stdin(Stdio::from(pty.slave.try_clone()?))
            .stdout(Stdio::from(pty.slave.try_clone()?))
            .stderr(Stdio::from(pty.slave))
            .kill_on_drop(true)
            .spawn()?;

        Ok((child, std::fs::File::from(pty.master)))
    }

    /// Include the colon in `name:` to target the session's window.
    /// tmux checks window names before session names. These windows all have the name `bwrap`.
    /// Previously, a bare `b` matched another session's `bwrap` window and resized it.
    pub async fn resize(&self, name: &str, cols: u16, rows: u16) -> Result<()> {
        let target = format!("{name}:");
        let cols = cols.to_string();
        let rows = rows.to_string();
        self.run(&["resize-window", "-t", &target, "-x", &cols, "-y", &rows])
            .await?;
        Ok(())
    }

    /// Read the terminal size that tmux preserves after the daemon stops.
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

    /// Use a separate tmux buffer for each session's large payloads.
    /// Measured with a raw-mode reader on tmux 3.7c: `send-keys -t probe:.0 -H`
    /// delivered 996 bytes; 997 delivered none and failed with "command too long".
    /// Load from stdin and paste in one ordered tmux command list. Paste with `-r`
    /// to preserve newlines. Delete the buffer after use.
    ///
    /// Bracketed paste mode groups a large paste into one terminal event.
    /// This prevents Codex from splitting the paste when PTY reads pause.
    pub async fn paste_bytes(&self, name: &str, bytes: &[u8]) -> Result<()> {
        use tokio::io::AsyncWriteExt;

        let _perf = crate::perf::timer("input-paste-tmux");

        let buf = format!("slopworld-{name}");
        let target = format!("{name}:.0");
        let mut child = Command::new("tmux")
            .args(self.socket_args())
            .args(["load-buffer", "-b", buf.as_str(), "-"])
            .arg(";")
            .args(["paste-buffer", "-d", "-r", "-p", "-b", &buf, "-t", &target])
            .stdin(Stdio::piped())
            .stdout(Stdio::null())
            .stderr(Stdio::piped())
            .spawn()?;

        // Close stdin before waiting. tmux waits for EOF, so an open stream would block completion.
        {
            let mut stdin = child.stdin.take().context("load-buffer stdin")?;
            stdin.write_all(bytes).await?;
            stdin.shutdown().await?;
        }
        let out = child.wait_with_output().await?;
        if !out.status.success() {
            bail!(
                "tmux paste failed: {}",
                crate::sandbox::sanitize_diagnostic(&String::from_utf8_lossy(&out.stderr))
            );
        }
        Ok(())
    }
}

/// Parse a `display-message` line with two coordinates, an alternate-screen flag, and a title.
/// Missing values use startup defaults. A failed pane query supplies an empty line through `unwrap_or_default()`.
fn parse_pos(pos: &str) -> (u16, u16, bool, String) {
    let mut it = pos.splitn(4, ' ');
    let cx = it.next().and_then(|v| v.parse().ok()).unwrap_or(0);
    let cy = it
        .next()
        .and_then(|v| v.trim_end().parse().ok())
        .unwrap_or(0);
    // tmux flags use `1` and `0`. bool::from_str accepts only `true` and `false`.
    // Compare the value directly to avoid treating both flags as false.
    let alt = it.next().map(|v| v.trim_end() == "1").unwrap_or(false);
    (cx, cy, alt, clean_title(it.next().unwrap_or("")))
}

/// Remove control characters from the title before sending it to a new emulator as an OSC sequence.
/// This prevents application titles from inserting escape sequences.
fn clean_title(s: &str) -> String {
    s.chars().filter(|c| !c.is_control()).take(512).collect()
}

fn parse_host_metadata_rows(mut output: &str) -> Option<HashMap<String, HostMetadata>> {
    let mut rows = HashMap::new();
    while !output.is_empty() {
        let (name, rest) = parse_length_framed_field(output)?;
        let name = name?;
        let (path, rest) = parse_length_framed_field(rest)?;
        let (command, rest) = parse_length_framed_field(rest)?;
        if rest.is_empty() {
            output = rest;
        } else {
            output = rest.strip_prefix('\n')?;
        }
        if path.is_some() || command.is_some() {
            rows.insert(name, HostMetadata { path, command });
        }
    }
    Some(rows)
}

/// tmux's `n:` format modifier reports a field's byte length. Framing values by that length
/// keeps tabs, newlines and colons inside either value from changing where the next field starts.
fn parse_length_framed_field(input: &str) -> Option<(Option<String>, &str)> {
    let colon = input.find(':')?;
    let length = input.get(..colon)?.parse::<usize>().ok()?;
    let value_start = colon + 1;
    let value_end = value_start.checked_add(length)?;
    let value = input.get(value_start..value_end)?;
    let rest = input.get(value_end..)?;
    let value = (!value.is_empty()).then(|| value.to_string());
    Some((value, rest))
}

#[cfg(test)]
mod tests;
