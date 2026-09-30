//! tmux server startup, readiness, and server-wide terminal options.

use super::*;

impl Tmux {
    /// `list-sessions` returns a nonzero exit code if the server is stopped or has no sessions.
    /// Only a stopped server produces the corresponding stderr message.
    pub(super) async fn server_running(&self) -> Result<bool> {
        let output = Command::new("tmux")
            .args(self.socket_args())
            .arg("list-sessions")
            .output()
            .await
            .context("checking tmux server")?;
        if output.status.success() {
            return Ok(true);
        }
        let error = String::from_utf8_lossy(&output.stderr);
        if error.trim() == "no sessions" {
            return Ok(true);
        }
        if error.starts_with("no server running on ")
            || (error.starts_with("error connecting to ")
                && (error.contains("(No such file or directory)")
                    || error.contains("(Connection refused)")))
        {
            return Ok(false);
        }
        bail!("cannot check tmux server: {}", error.trim());
    }

    /// tmux inherits the caller's cgroup unless it starts in a separate systemd user unit.
    /// `tmux start-server` forks, so a scope can terminate before the server.
    /// Use a `Type=forking` unit. Verify the socket before proceeding.
    /// If systemd is unavailable, start the server directly.
    pub async fn ensure_server(&self) -> Result<()> {
        if self.server_running().await? {
            self.configure_emoji_widths().await;
            return Ok(());
        }

        // Independent sockets must not compete for a fixed unit, and a recently
        // stopped server's unit may still be waiting for systemd to collect it.
        let unit_name = format!("slopworld-tmux-{}", uuid::Uuid::new_v4());
        let unit = self.start_server_unit(&unit_name).await;

        // systemd-run returns when it queues the job. Check the socket to confirm server startup.
        let mut up = false;
        if matches!(&unit, Ok(s) if s.success()) {
            for _ in 0..20 {
                if self.server_running().await? {
                    up = true;
                    break;
                }
                tokio::time::sleep(std::time::Duration::from_millis(100)).await;
            }
        }

        if up {
            tracing::info!(unit = %unit_name, "tmux server started in a transient user service");
        } else {
            match unit {
                Ok(s) if s.success() => {
                    tracing::warn!(unit = %unit_name, "The tmux unit started no server. slopd will start tmux inline.")
                }
                Ok(s) => tracing::warn!("systemd-run failed ({s}). slopd will start tmux inline."),
                Err(_) => {
                    tracing::warn!("systemd-run is unavailable. slopd will start tmux inline.")
                }
            }
            self.run(&["start-server", ";", "set-option", "-s", "exit-empty", "off"])
                .await
                .context("starting tmux inline")?;
            if !self.server_running().await? {
                bail!("tmux inline startup left no ready server");
            }
        }

        // Panes inherit this at creation, so it has to be in place before the first
        // session is spawned.
        let limit = crate::tmux::SCROLLBACK_LINES.to_string();
        drop(
            self.run(&["set-option", "-g", "history-limit", &limit])
                .await,
        );
        self.configure_emoji_widths().await;
        Ok(())
    }

    async fn start_server_unit(&self, name: &str) -> std::io::Result<std::process::ExitStatus> {
        let mut command = Command::new("systemd-run");
        command
            .args(["--user", "--quiet", "--collect"])
            .arg(format!("--unit={name}"))
            .arg("--property=Type=forking");
        // Services inherit the manager's environment, not the client's. The
        // server and every client must resolve -L against the same socket root.
        if std::env::var_os("TMUX_TMPDIR").is_some() {
            command.arg("--setenv=TMUX_TMPDIR");
        }
        command
            .args([
                "--",
                "tmux",
                self.socket_args()[0],
                self.socket_args()[1],
                "start-server",
                ";",
                "set-option",
                "-s",
                "exit-empty",
                "off",
            ])
            .status()
            .await
    }

    async fn configure_emoji_widths(&self) {
        if let Err(error) = self
            .run(&[
                "set-option",
                "-s",
                "codepoint-widths[0]",
                EMOJI_MODIFIER_WIDTHS,
            ])
            .await
        {
            tracing::warn!("cannot set tmux emoji modifier widths: {error:#}");
        }
    }
}

#[cfg(test)]
#[path = "server_tests.rs"]
mod tests;
