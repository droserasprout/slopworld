//! How the game is launched, and whether it is up.
//!
//! Nothing inside a session can answer the second half: an agent runs in its own PID
//! namespace, so `pgrep` there reads as "no game is running" rather than "cannot tell". The
//! attached client count is in the answer because the question is usually "is it running the
//! mod I just built", which the process table cannot prove.

use std::process::Command;

use anyhow::Result;
use serde::Serialize;

const UNIT: &str = "slopworld-game.service";

#[derive(Debug, Clone, Serialize)]
pub struct Status {
    pub running: bool,
    /// `unit` is one slopd started, `process` one found by command line, `client` one
    /// we could not find either way but that is holding /ws open.
    pub source: &'static str,
    pub pid: Option<u32>,
    pub uptime_s: Option<u64>,
    pub unit: Option<&'static str>,
    /// So a caller getting `none` can see whether that is because nothing is
    /// configured.
    pub cmd: String,
    /// Anything holding /ws open counts; the daemon cannot tell a mod from a curl.
    pub clients: usize,
    /// Across a redeploy this is the useful number: a client younger than the DLL on
    /// disk is the build you just installed.
    pub clients_uptime_s: Option<u64>,
}

pub fn status(cmd: &str, clients: usize, clients_uptime_s: Option<u64>) -> Status {
    let argv = crate::sandbox::shell_split(cmd);
    let exe = argv
        .first()
        .map(|s| crate::config::expand(s))
        .unwrap_or_default();

    let (source, pid) = match unit_pid() {
        Some(pid) => ("unit", Some(pid)),
        None => match found(&exe) {
            Some(pid) => ("process", Some(pid)),
            None if clients > 0 => ("client", None),
            None => ("none", None),
        },
    };

    Status {
        running: source != "none",
        source,
        pid,
        uptime_s: pid.and_then(uptime_s),
        unit: (source == "unit").then_some(UNIT),
        cmd: cmd.to_string(),
        clients,
        clients_uptime_s,
    }
}

/// `--collect` clears the unit away when the game exits, so a `show` of a unit
/// that never existed answers the same as one that has finished: inactive, PID 0.
fn unit_pid() -> Option<u32> {
    let out = Command::new("systemctl")
        .args([
            "--user",
            "show",
            UNIT,
            "--property=ActiveState",
            "--property=MainPID",
        ])
        .output()
        .ok()?;
    let text = String::from_utf8_lossy(&out.stdout);

    let mut active = false;
    let mut pid = 0u32;
    for line in text.lines() {
        match line.split_once('=') {
            Some(("ActiveState", v)) => active = v == "active" || v == "activating",
            Some(("MainPID", v)) => pid = v.trim().parse().unwrap_or(0),
            _ => {}
        }
    }
    (active && pid > 0).then_some(pid)
}

/// The configured path first, then the executable's bare name - which would also match an
/// editor with the word in its argv, hence the fallback and not the test.
///
/// The path is matched *anchored*: `pgrep -f` tries its pattern anywhere in a command line,
/// and every sandbox binds `<game>/RimWorldLinux_Data/Managed`, so a bare `-f <path>` matches
/// an agent and a restart then waits forever for a PID that was never the game.
fn found(exe: &str) -> Option<u32> {
    if exe.is_empty() {
        return None;
    }
    let name = exe.rsplit('/').next().unwrap_or(exe);
    pgrep(&["-f", &argv0_pattern(exe)]).or_else(|| pgrep(&["-x", name]))
}

/// The path is a string somebody typed, not a pattern, so every character a regex
/// would read is escaped first.
fn argv0_pattern(exe: &str) -> String {
    let mut out = String::with_capacity(exe.len() + 8);
    out.push('^');
    for c in exe.chars() {
        if "\\.^$*+?()[]{}|".contains(c) {
            out.push('\\');
        }
        out.push(c);
    }
    out.push_str("( |$)");
    out
}

fn pgrep(args: &[&str]) -> Option<u32> {
    let out = Command::new("pgrep").args(args).output().ok()?;
    String::from_utf8_lossy(&out.stdout)
        .lines()
        .find_map(|l| l.trim().parse().ok())
}

/// Field 22 of `stat` is the start in clock ticks since boot. Everything before the last `)`
/// is skipped, the process name sitting in there unescaped.
pub fn uptime_s(pid: u32) -> Option<u64> {
    let stat = std::fs::read_to_string(format!("/proc/{pid}/stat")).ok()?;
    let fields = &stat[stat.rfind(')')? + 1..];
    // The first field after the name is `state`, which is number 3.
    let ticks: f64 = fields.split_whitespace().nth(22 - 3)?.parse().ok()?;
    let up: f64 = std::fs::read_to_string("/proc/uptime")
        .ok()?
        .split_whitespace()
        .next()?
        .parse()
        .ok()?;
    // USER_HZ is 100 for this interface whatever the kernel's own tick is.
    Some((up - ticks / 100.0).max(0.0) as u64)
}

/// Starts a process that must outlive slopd: a plain child would sit in `slopd.service`'s
/// cgroup and be killed by the next redeploy, which is exactly when the game is being
/// relaunched. `--collect` frees the unit name for the next restart. The three display
/// variables are passed explicitly so a daemon run by hand from a terminal works too.
pub fn launch(exe: &str, args: &[String]) -> Result<()> {
    // `game_cmd` is a path a person typed, so it can start with a ~ that nothing else
    // expands: shell_split builds an argv rather than running a shell.
    let exe = &crate::config::expand(exe);

    let mut sr = Command::new("systemd-run");
    sr.args(["--user", "--quiet", "--collect", &format!("--unit={UNIT}")]);
    for k in [
        "DISPLAY",
        "WAYLAND_DISPLAY",
        "XAUTHORITY",
        "XDG_RUNTIME_DIR",
    ] {
        if let Ok(v) = std::env::var(k) {
            sr.arg(format!("--setenv={k}={v}"));
        }
    }
    sr.arg("--").arg(exe).args(args);

    match sr.status() {
        Ok(s) if s.success() => return Ok(()),
        Ok(s) => tracing::warn!("systemd-run failed ({s}); launching inline"),
        Err(e) => tracing::warn!("systemd-run unavailable ({e}); launching inline"),
    }

    // Its own process group, so a signal aimed at slopd's does not take the game.
    use std::os::unix::process::CommandExt;
    Command::new(exe)
        .args(args)
        .stdin(std::process::Stdio::null())
        .stdout(std::process::Stdio::null())
        .stderr(std::process::Stdio::null())
        .process_group(0)
        .spawn()?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A name with a space and a bracket in it is what breaks a stat line parsed from
    /// the left, and the kernel does not escape it.
    #[test]
    fn our_own_uptime_is_readable() {
        let mine = uptime_s(std::process::id()).expect("a running process has an uptime");
        assert!(
            mine < 60 * 60 * 24 * 365,
            "{mine}s is not a plausible test run"
        );
    }

    #[test]
    fn nothing_configured_is_not_a_running_game() {
        let s = status("", 0, None);
        assert!(!s.running);
        assert_eq!(s.source, "none");
        assert_eq!(s.pid, None);
    }

    /// The executable is named after nothing, because `found` falls back to the bare
    /// name and `RimWorldLinux` would find the real game - so a test written with the
    /// true name passes only on a machine that is not playing.
    #[test]
    fn a_configured_path_is_expanded_before_it_is_looked_for() {
        let s = status("~/nowhere/SlopdNoSuchBinary -popupwindow", 0, None);
        assert_eq!(s.source, "none");
        assert!(s.cmd.starts_with('~'), "the config is reported as written");
    }

    #[test]
    fn a_client_is_evidence() {
        let s = status("", 1, Some(3));
        assert!(s.running);
        assert_eq!(s.source, "client");
        assert_eq!(s.clients_uptime_s, Some(3));
    }

    /// Without the `^` a bind path anywhere in an agent's argv matches; without the
    /// `( |$)` the game's own `RimWorldLinux_Data` does.
    #[test]
    fn the_game_is_matched_as_a_whole_argv0() {
        assert_eq!(
            argv0_pattern("/home/me/RimWorld/game/RimWorldLinux"),
            "^/home/me/RimWorld/game/RimWorldLinux( |$)"
        );
    }

    #[test]
    fn a_dot_in_the_path_is_a_dot() {
        assert_eq!(argv0_pattern("/o.d/rw"), "^/o\\.d/rw( |$)");
    }

    /// The bug, against the thing that actually runs the pattern.
    #[test]
    fn pgrep_does_not_take_a_bind_path_for_the_game() {
        let exe = "/home/nobody/RimWorld/game/RimWorldLinux";
        // A shell that cannot tail-exec its script, so the argv we gave it stays on
        // /proc.
        let mut decoy = match Command::new("sh")
            .args(["-c", "while :; do sleep 1; done"])
            .arg(format!("bwrap --ro-bind {exe}_Data/Managed /x -- claude"))
            .spawn()
        {
            Ok(c) => c,
            Err(_) => return, // no shell here; the string tests still hold
        };
        // `spawn` returns before the child has exec'd, so its argv is not on /proc yet.
        let mut decoy_is_findable = None;
        for _ in 0..50 {
            decoy_is_findable = pgrep(&["-f", exe]);
            if decoy_is_findable.is_some() {
                break;
            }
            std::thread::sleep(std::time::Duration::from_millis(20));
        }
        let hit = pgrep(&["-f", &argv0_pattern(exe)]);
        let _ = decoy.kill();
        let _ = decoy.wait();

        assert_eq!(
            decoy_is_findable,
            Some(decoy.id()),
            "the decoy is what the old pattern found"
        );
        assert_eq!(hit, None, "and it is not the game");
    }
}
