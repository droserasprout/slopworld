//! Exercise the real daemon's publication and signal cleanup with private filesystem and tmux roots.
#![cfg(test)]
use std::fs;
use std::net::{SocketAddr, TcpListener};
use std::os::unix::fs::PermissionsExt;
use std::path::PathBuf;
use std::process::{Child, Command, ExitStatus, Stdio};
use std::time::{Duration, Instant};

#[path = "support/daemon_recovery.rs"]
mod recovery;

#[expect(
    dead_code,
    reason = "the shared wire schema includes other API operations"
)]
mod wire {
    include!(concat!(env!("OUT_DIR"), "/slopworld.rs"));
}

struct Daemon {
    root: PathBuf,
    address: SocketAddr,
    child: Option<Child>,
}

impl Daemon {
    fn new(address: SocketAddr) -> Self {
        let root = std::env::temp_dir().join(format!("slopd-lifecycle-{}", uuid::Uuid::new_v4()));
        fs::create_dir_all(root.join("config")).unwrap();
        fs::write(
            root.join("config/config.toml"),
            format!("[daemon]\nbind = '{address}'\ntoken = 'lifecycle-secret'\n"),
        )
        .unwrap();
        Self {
            root,
            address,
            child: None,
        }
    }

    fn endpoint(&self) -> PathBuf {
        self.root.join("endpoint.toml")
    }

    fn tmux(&self, args: &[&str]) -> std::process::Output {
        Command::new("tmux")
            .env("TMUX_TMPDIR", &self.root)
            .args(["-L", "lifecycle"])
            .args(args)
            .output()
            .unwrap()
    }

    fn start(&mut self) {
        assert!(self.child.is_none());
        let log = fs::File::create(self.root.join("daemon.log")).unwrap();
        self.child = Some(
            Command::new(env!("CARGO_BIN_EXE_slopd"))
                .env("SLOPD_CONFIG_ROOT", self.root.join("config"))
                .env("SLOPD_DATA", self.root.join("data"))
                .env("SLOPD_CACHE", self.root.join("cache"))
                .env("SLOPD_ENDPOINT", self.endpoint())
                .env("TMUX_TMPDIR", &self.root)
                .env("SLOPD_TMUX_SOCKET", "lifecycle")
                .env_remove("SLOPD_RUNTIME")
                .env_remove("OPENROUTER_API_KEY")
                .env("NO_PROXY", "*")
                .env("no_proxy", "*")
                .stdin(Stdio::null())
                .stdout(log.try_clone().unwrap())
                .stderr(log)
                .spawn()
                .unwrap(),
        );
    }

    fn logs(&self) -> String {
        fs::read_to_string(self.root.join("daemon.log")).unwrap_or_default()
    }

    fn health(&self, token: &str) -> Result<ureq::http::StatusCode, ureq::Error> {
        ureq::get(&format!("http://{}/api/health", self.address))
            .config()
            .http_status_as_error(false)
            .timeout_global(Some(Duration::from_millis(250)))
            .build()
            .header("x-slop-token", token)
            .call()
            .map(|response| response.status())
    }

    fn ready(&mut self) {
        let deadline = Instant::now() + Duration::from_secs(15);
        loop {
            assert!(
                self.child.as_mut().unwrap().try_wait().unwrap().is_none(),
                "daemon exited before readiness: {}",
                self.logs()
            );
            if self.endpoint().exists()
                && self
                    .health("lifecycle-secret")
                    .is_ok_and(|status| status == 200)
            {
                return;
            }
            assert!(
                Instant::now() < deadline,
                "daemon never became ready: {}",
                self.logs()
            );
            std::thread::sleep(Duration::from_millis(20));
        }
    }

    fn wait(&mut self) -> ExitStatus {
        let deadline = Instant::now() + Duration::from_secs(10);
        loop {
            if let Some(status) = self.child.as_mut().unwrap().try_wait().unwrap() {
                self.child.take();
                return status;
            }
            assert!(
                Instant::now() < deadline,
                "daemon did not exit: {}",
                self.logs()
            );
            std::thread::sleep(Duration::from_millis(20));
        }
    }
}

impl Drop for Daemon {
    fn drop(&mut self) {
        if let Some(mut child) = self.child.take() {
            drop(child.kill());
            drop(child.wait());
        }
        drop(self.tmux(&["kill-server"]));
        drop(fs::remove_dir_all(&self.root));
    }
}

#[test]
fn lifecycle_signals_remove_discovery_and_preserve_tmux_across_restart() {
    let reservation = TcpListener::bind("127.0.0.1:0").unwrap();
    let mut daemon = Daemon::new(reservation.local_addr().unwrap());
    let output = daemon.tmux(&[
        "-f",
        "/dev/null",
        "new-session",
        "-d",
        "-s",
        "survivor",
        "sleep 120",
    ]);
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
    let pane = daemon
        .tmux(&["display-message", "-p", "-t", "survivor", "#{pane_pid}"])
        .stdout;
    drop(reservation);
    for signal in ["TERM", "INT"] {
        assert!(!daemon.endpoint().exists());
        daemon.start();
        daemon.ready();
        let endpoint: toml::Value =
            toml::from_str(&fs::read_to_string(daemon.endpoint()).unwrap()).unwrap();
        assert_eq!(
            endpoint["url"].as_str(),
            Some(format!("http://{}", daemon.address).as_str())
        );
        assert_eq!(endpoint["token"].as_str(), Some("lifecycle-secret"));
        assert_eq!(
            fs::metadata(daemon.endpoint())
                .unwrap()
                .permissions()
                .mode()
                & 0o777,
            0o600
        );
        assert_eq!(daemon.health("wrong").unwrap(), 401);
        assert!(
            Command::new("kill")
                .args([
                    "-s",
                    signal,
                    &daemon.child.as_ref().unwrap().id().to_string()
                ])
                .status()
                .unwrap()
                .success()
        );
        assert!(daemon.wait().success(), "{}", daemon.logs());
        assert!(
            !daemon.endpoint().exists(),
            "stale endpoint after SIG{signal}"
        );
        assert!(
            daemon
                .tmux(&["has-session", "-t", "survivor"])
                .status
                .success()
        );
        assert_eq!(
            daemon
                .tmux(&["display-message", "-p", "-t", "survivor", "#{pane_pid}"])
                .stdout,
            pane
        );
        assert!(
            TcpListener::bind(daemon.address).is_ok(),
            "listener survived shutdown"
        );
    }
}

#[test]
fn lifecycle_occupied_listener_fails_without_publishing_discovery_or_starting_tmux() {
    let reservation = TcpListener::bind("127.0.0.1:0").unwrap();
    let mut daemon = Daemon::new(reservation.local_addr().unwrap());
    daemon.start();
    assert!(!daemon.wait().success());
    assert!(
        daemon.logs().contains("reserving daemon endpoint"),
        "{}",
        daemon.logs()
    );
    assert!(!daemon.endpoint().exists());
    assert!(!fs::read_dir(&daemon.root).unwrap().any(|entry| {
        entry
            .unwrap()
            .file_name()
            .to_string_lossy()
            .starts_with("tmux-")
    }));
    assert!(!daemon.root.join("data").exists());
}

#[test]
fn board_cli_edits_are_transactional_and_survive_restart() {
    let reservation = TcpListener::bind("127.0.0.1:0").unwrap();
    let mut daemon = Daemon::new(reservation.local_addr().unwrap());
    drop(reservation);
    use prost::Message;
    let project = "1111111111111111";
    let configs = daemon.root.join("config/projects");
    fs::create_dir_all(&configs).unwrap();
    fs::write(
        configs.join(format!("{project}.toml")),
        format!("storage_order = 0\nid = '{project}'\nname = 'Repo'\ndir = '/tmp'\n"),
    )
    .unwrap();
    daemon.start();
    daemon.ready();
    let board = |daemon: &Daemon, args: &[&str]| -> serde_json::Value {
        let output = Command::new(env!("CARGO_BIN_EXE_slopctl"))
            .arg("board")
            .args(args)
            .env("SLOPD_URL", format!("http://{}", daemon.address))
            .env("SLOPD_TOKEN", "lifecycle-secret")
            .env("SLOPWORLD_SESSION", "host")
            .output()
            .unwrap();
        assert!(
            output.status.success(),
            "{}",
            String::from_utf8_lossy(&output.stderr)
        );
        serde_json::from_slice(&output.stdout).unwrap()
    };
    assert!(board(&daemon, &["list"]).as_array().unwrap().is_empty());
    ureq::post(&format!("http://{}/api/boards", daemon.address))
        .header("x-slop-token", "lifecycle-secret")
        .header("content-type", "application/x-protobuf")
        .send(
            wire::BoardEdit {
                board: project.into(),
                action: "enable-board".into(),
                text: "Repo".into(),
                ..Default::default()
            }
            .encode_to_vec()
            .as_slice(),
        )
        .unwrap();
    let added = board(&daemon, &["add-column", project, "Review"]);
    let columns = added["boards"][0]["columns"].as_array().unwrap();
    let backlog = columns[0]["id"].as_str().unwrap();
    let review = columns[4]["id"].as_str().unwrap();
    let added = board(&daemon, &["add-card", project, backlog, "Check protocol"]);
    let card = added["boards"][0]["columns"][0]["cards"][0]["id"]
        .as_str()
        .unwrap();
    board(&daemon, &["assign", project, card, "reviewer"]);
    board(&daemon, &["link", project, card, "worktree", "checkout"]);
    board(&daemon, &["comment", project, card, "Ready"]);
    board(&daemon, &["move-card", project, review, card]);
    board(
        &daemon,
        &["move-column", project, review, "--before", backlog],
    );
    // Inspect through an independent connection while the daemon is live.
    {
        let db = rusqlite::Connection::open(daemon.root.join("data/boards.sqlite3")).unwrap();
        let location: String = db
            .query_row("SELECT col FROM cards WHERE id=?", [card], |r| r.get(0))
            .unwrap();
        assert_eq!(location, review);
    }
    assert!(
        Command::new("kill")
            .args(["-TERM", &daemon.child.as_ref().unwrap().id().to_string()])
            .status()
            .unwrap()
            .success()
    );
    assert!(daemon.wait().success());
    daemon.start();
    daemon.ready();
    let shown = board(&daemon, &["show"]);
    let reviewed = &shown["columns"][0]["cards"][0];
    assert_eq!(reviewed["id"], card);
    assert_eq!(reviewed["assignee"], "reviewer");
    assert_eq!(reviewed["comments"][0]["text"], "Ready");
    assert_eq!(reviewed["links"][0]["target"], "checkout");
}
