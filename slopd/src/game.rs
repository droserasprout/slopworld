//! What the daemon can say about the game: how it is launched, and whether it is
//! up.
//!
//! The second half exists because nothing inside a session can answer it. An
//! agent runs in a PID namespace of its own, so `ps` and `pgrep` in there see
//! the agent's own handful of processes and nothing else on the host - which
//! does not read as "cannot tell", it reads as "no game is running". Sharing
//! the PID namespace would fix the symptom and hand every agent the ability to
//! signal every process the user owns; slopd is on the host and already the
//! thing sessions ask about everything else.
//!
//! So: `GET /api/game`. The question behind it is usually not "is a game
//! running" but "is the game running the mod I just built", which is why the
//! attached client count is in the answer - a websocket client is a game far
//! enough up to have loaded our assembly, which no amount of looking at the
//! process table proves.

use std::process::Command;

use anyhow::Result;
use serde::Serialize;

/// The transient unit `launch` puts the game in, and so the first place `status`
/// looks for it.
const UNIT: &str = "slopworld-game.service";

#[derive(Debug, Clone, Serialize)]
pub struct Status {
    pub running: bool,
    /// How we know: a game slopd started has a unit and a PID, a game started by
    /// hand is one we found by its command line, and a client on the socket is
    /// proof of a game we could not find either way (`daemon.game_cmd` unset,
    /// or a launcher that execs something else).
    pub source: &'static str,
    pub pid: Option<u32>,
    /// Seconds it has been up, not the instant it started - the same choice the
    /// usage window resets make: an age keeps meaning what it meant when the
    /// reader's clock is not ours.
    pub uptime_s: Option<u64>,
    pub unit: Option<&'static str>,
    /// What `daemon.game_cmd` says to run, so a caller getting `none` can see
    /// whether that is because nothing is configured.
    pub cmd: String,
    /// Websocket clients attached right now. Normally the mod, and exactly one
    /// of it; anything holding /ws open counts, because the daemon cannot tell
    /// a mod from a curl and should not pretend to.
    pub clients: usize,
    /// How long the oldest of them has been attached. Across a redeploy this is
    /// the useful number in here: a client younger than the game is a mod that
    /// reconnected, and one younger than the DLL on disk is the build you just
    /// installed.
    pub clients_uptime_s: Option<u64>,
}

/// Looks for the game, cheapest road first.
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

/// The unit's main PID, if it is up. `--collect` clears the unit away when the
/// game exits, so a `show` of a unit that never existed answers the same as one
/// that has finished: inactive, MainPID 0.
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

/// The game as launched by somebody else - which is the normal case while
/// working on the mod, since the game gets started once and then only ever
/// restarted through `/api/game/restart`.
///
/// Matched on the configured path first, because that is what was asked for;
/// then on the executable's own name, which catches a launcher that starts the
/// same binary by another path but would also match an editor that happens to
/// have the name in its argv - so it is the fallback and not the test.
///
/// The path is matched *anchored*, and that is not tidiness. `pgrep -f` tries
/// its pattern anywhere in a command line, and the game's own directory turns
/// up in command lines that are not the game: every sandbox binds
/// `<game>/RimWorldLinux_Data/Managed` so an agent can build the mod against the
/// game's assemblies, so a bare `-f <path>` matches an agent. Nothing says so
/// until a restart, which quits the game, waits for that PID to go away, finds
/// it still there because it was never the game, and refuses to launch. Bounded
/// at both ends, only a process actually exec'd from that path can match.
fn found(exe: &str) -> Option<u32> {
    if exe.is_empty() {
        return None;
    }
    let name = exe.rsplit('/').next().unwrap_or(exe);
    pgrep(&["-f", &argv0_pattern(exe)]).or_else(|| pgrep(&["-x", name]))
}

/// `exe` as an extended regex that matches only a command line starting with it.
/// The path is a string somebody typed, not a pattern, so every character a
/// regex would read is escaped first.
fn argv0_pattern(exe: &str) -> String {
    let mut out = String::with_capacity(exe.len() + 8);
    out.push('^');
    for c in exe.chars() {
        if "\\.^$*+?()[]{}|".contains(c) {
            out.push('\\');
        }
        out.push(c);
    }
    // End of the command line, or the space before the first argument.
    out.push_str("( |$)");
    out
}

fn pgrep(args: &[&str]) -> Option<u32> {
    let out = Command::new("pgrep").args(args).output().ok()?;
    String::from_utf8_lossy(&out.stdout)
        .lines()
        .find_map(|l| l.trim().parse().ok())
}

/// Seconds since a PID started, from procfs rather than from `ps`: field 22 of
/// `stat` is the start in clock ticks since boot, and /proc/uptime is where boot
/// was. Everything before the last `)` is skipped because the process name sits
/// in there unescaped, parentheses and spaces included, and splitting the line
/// from the left is how that bites.
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

/// Starts a process that must outlive slopd, as a transient systemd unit where
/// there is one.
///
/// Spawned as a plain child it would sit in `slopd.service`'s cgroup and be
/// killed by the next redeploy - the same trap the tmux server was in, and worse
/// here, because the whole point of relaunching the game is that a redeploy is
/// happening. `--collect` clears the unit away once the game exits, so the name
/// is free for the next restart.
///
/// The display environment comes from slopd's own: as a user service it was
/// started by the same manager as the session, so it has whatever that manager
/// imported. Passing the three that matter explicitly means a daemon run by hand
/// from a terminal works too.
pub fn launch(exe: &str, args: &[String]) -> Result<()> {
    // `game_cmd` is a path a person typed into config.toml, so it can start with
    // a ~ that nothing else here would expand: shell_split builds an argv rather
    // than running a shell, and exec does not read tildes. Left alone it fails
    // four seconds after a redeploy told the game to quit.
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

    // No systemd: at least give it its own process group, so a signal aimed at
    // slopd's group doesn't take the game with it.
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

    /// A name with a space and a bracket in it is the case that breaks a stat
    /// line parsed from the left, and the kernel does not escape it.
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

    /// The same expansion `launch` does, so what we look for is what was run.
    #[test]
    fn a_configured_path_is_expanded_before_it_is_looked_for() {
        let s = status("~/nowhere/RimWorldLinux -popupwindow", 0, None);
        assert_eq!(s.source, "none");
        assert!(s.cmd.starts_with('~'), "the config is reported as written");
    }

    /// A client on the socket is a game we could not find any other way, and
    /// saying so is the point of `source`.
    #[test]
    fn a_client_is_evidence() {
        let s = status("", 1, Some(3));
        assert!(s.running);
        assert_eq!(s.source, "client");
        assert_eq!(s.clients_uptime_s, Some(3));
    }

    /// Both ends of the pattern, which is the whole of the fix: without the `^`
    /// a bind path anywhere in an agent's argv matches, and without the `( |$)`
    /// the game's own `RimWorldLinux_Data` does.
    #[test]
    fn the_game_is_matched_as_a_whole_argv0() {
        assert_eq!(
            argv0_pattern("/home/me/RimWorld/game/RimWorldLinux"),
            "^/home/me/RimWorld/game/RimWorldLinux( |$)"
        );
    }

    /// A path with a regex character in it is still a path.
    #[test]
    fn a_dot_in_the_path_is_a_dot() {
        assert_eq!(argv0_pattern("/o.d/rw"), "^/o\\.d/rw( |$)");
    }

    /// The bug, against the thing that actually runs the pattern: a process
    /// whose argv merely mentions the game, the way every sandbox's does.
    #[test]
    fn pgrep_does_not_take_a_bind_path_for_the_game() {
        let exe = "/home/nobody/RimWorld/game/RimWorldLinux";
        // A shell that cannot tail-exec its script, so the argv we gave it is
        // the argv on /proc for as long as we need it.
        let mut decoy = match Command::new("sh")
            .args(["-c", "while :; do sleep 1; done"])
            .arg(format!("bwrap --ro-bind {exe}_Data/Managed /x -- claude"))
            .spawn()
        {
            Ok(c) => c,
            Err(_) => return, // no shell here; the string tests still hold
        };
        // `spawn` returns before the child has exec'd, so its argv is not on
        // /proc yet; without this the test proves only that pgrep found nothing.
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
