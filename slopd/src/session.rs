use std::collections::HashMap;
use std::hash::{Hash, Hasher};
use std::path::PathBuf;
use std::sync::atomic::{AtomicU64, AtomicUsize, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use anyhow::{anyhow, bail, Context, Result};
use regex::Regex;
use serde::{Deserialize, Serialize};
use tokio::sync::{broadcast, RwLock};
use tokio::task::JoinHandle;
use tokio::time::{interval, MissedTickBehavior};

use crate::config::{expand, Config, Daemon, Defaults, ProjectCfg, Sandbox, SessionCfg};
use crate::emu::{parse_output, Frame, MouseInput, SessionEmu};
use crate::sandbox::build_argv;
use crate::tmux::Tmux;

/// A pane quiet for this long has gone idle. Matches the mod's rule for putting
/// an idle agent's colonist to sleep.
const IDLE_MS: u64 = 10_000;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum State {
    /// tmux session is gone: either never started or the agent exited. The mod
    /// puts the colonist behind it on the floor rather than killing it, so the
    /// same process can get the same body back up.
    Down,
    /// Pane text is changing, or a rule says the agent is mid-turn.
    Working,
    /// Agent is blocked on the human. This is the one the player must notice.
    Waiting,
    /// Alive, quiet, nothing to do.
    Idle,
}

#[derive(Debug, Clone, Serialize)]
pub struct SessionView {
    pub name: String,
    /// The project this agent works in, and where that project lives. `dir`,
    /// `net` and `sandbox` are all the project's answers, repeated here so a
    /// list of sessions reads without joining it against anything - and empty
    /// when the entry names a project that has gone, which is a state the GUI
    /// has to be able to draw rather than one to refuse to serve.
    pub project: String,
    pub dir: String,
    /// "claude" or "custom": what the session runs, and whether the sandbox
    /// hands it Claude Code's own state dir.
    pub kind: String,
    /// The command as it will actually be exec'd, defaults resolved.
    pub agent: String,
    pub state: State,
    pub alive: bool,
    /// The pane's size right now, not a setting: the window it is drawn in owns
    /// this and sends `resize` when it changes. Reported so `curl /api/sessions`
    /// can answer "what shape does the agent think its terminal is".
    pub cols: u16,
    pub rows: u16,
    pub net: bool,
    pub sandbox: bool,
    /// Sent so an edit round-trips it instead of quietly clearing it.
    pub autostart: bool,
    /// Unix millis of the last observed pane change.
    pub last_change: u64,
    pub seq: u64,
}

#[derive(Debug, Clone, Serialize)]
pub struct ScreenView {
    pub name: String,
    pub seq: u64,
    pub cols: u16,
    pub rows: u16,
    pub cx: u16,
    pub cy: u16,
    /// Lines scrolled up into scrollback; 0 for a live bottom frame. A scrolled
    /// frame is a one-off answer to a wheel event, never broadcast.
    #[serde(default)]
    pub off: u16,
    /// Cursor shape: 0 = block, 1 = underline, 2 = beam.
    #[serde(default)]
    pub cursor_shape: u8,
    /// Whether the app wants the cursor to blink.
    #[serde(default)]
    pub cursor_blink: bool,
    /// The app wants mouse reports (drives Phase 3 wheel/click forwarding).
    #[serde(default)]
    pub app_mouse: bool,
    /// The app is on the alternate screen (no scrollback of its own).
    #[serde(default)]
    pub alt_screen: bool,
    /// One entry per row, still carrying SGR escapes.
    pub lines: Vec<String>,
}

impl ScreenView {
    /// Builds a view from a freshly-rendered frame, carrying over every mode flag.
    fn from_frame(name: &str, seq: u64, cols: u16, rows: u16, off: u16, frame: Frame) -> Self {
        Self {
            name: name.to_string(),
            seq,
            cols,
            rows,
            cx: frame.cx,
            cy: frame.cy,
            off,
            cursor_shape: frame.cursor_shape,
            cursor_blink: frame.cursor_blink,
            app_mouse: frame.app_mouse,
            alt_screen: frame.alt_screen,
            lines: frame.lines,
        }
    }
}

#[derive(Debug, Clone, Serialize)]
#[serde(tag = "t", rename_all = "lowercase")]
pub enum Event {
    Sessions { sessions: Vec<SessionView> },
    /// The projects, whenever one is added, edited or removed - and on connect,
    /// because the dialog that picks one has to be able to draw before anybody
    /// has changed anything.
    Projects { projects: Vec<ProjectCfg> },
    Screen { screen: ScreenView },
    /// What is left of the subscription. Broadcast on a change only; the poll
    /// behind it runs on the wall clock, not on anything a session did.
    Usage { usage: crate::usage::Snapshot },
    /// Save the colony and quit, because the daemon is about to start the game
    /// again. The only event that asks the mod for something rather than telling
    /// it something, and it exists because nothing outside the game can save a
    /// colony: the daemon can kill the process, but a killed RimWorld comes back
    /// at the last autosave with a terminal nobody chose.
    Quit,
}

struct Live {
    cfg: SessionCfg,
    state: State,
    seq: u64,
    hash: u64,
    last_change: u64,
    cols: u16,
    rows: u16,
    screen: Option<ScreenView>,
    /// The live terminal emulator, present only while a control reader is
    /// attached (i.e. the session is running).
    emu: Option<Arc<Mutex<SessionEmu>>>,
    /// Handle to the control-mode reader task; aborted on stop/death.
    reader: Option<JoinHandle<()>>,
}

pub struct Manager {
    pub tmux: Tmux,
    pub cfg_path: PathBuf,
    cfg: RwLock<Config>,
    live: RwLock<HashMap<String, Live>>,
    rules: RwLock<Vec<(State, Regex)>>,
    /// mtime of config.toml as we last wrote or read it. Anything else means
    /// someone edited the file behind us; see `reload_if_changed`.
    cfg_mtime: Mutex<Option<SystemTime>>,
    /// Unix millis of the last mtime check, so the retick doesn't stat the file
    /// eighty times a second.
    cfg_checked: Mutex<u64>,
    /// Last thing the usage poller heard back. Lives here rather than in the
    /// poller so a client connecting between polls has something to draw.
    usage: RwLock<crate::usage::Snapshot>,
    /// Websocket clients attached right now, and when the count last left zero.
    /// Only `/api/game` reads them, and it reads them as evidence: a client is a
    /// game up far enough to have loaded the mod and talked to us.
    clients: AtomicUsize,
    clients_since: AtomicU64,
    pub events: broadcast::Sender<Event>,
}

/// One attached websocket client, counted for as long as the value lives. A
/// guard rather than a pair of calls because the pump has several ways out - a
/// dropped socket, a send error, the client going away mid-event - and every one
/// of them has to put the count back.
pub struct ClientGuard(Arc<Manager>);

impl Drop for ClientGuard {
    fn drop(&mut self) {
        self.0.clients.fetch_sub(1, Ordering::Relaxed);
    }
}

/// How often the config file is checked for outside edits.
const CFG_CHECK_MS: u64 = 2_000;

/// Half-second polls to wait for the game to exit before giving up on relaunching
/// it - a minute, which is a long shutdown and a short time to sit looking at a
/// game that has not come back.
const QUIT_WAIT_TICKS: usize = 120;

/// The size a pane is born at, and the only place a number like this lives now.
///
/// It used to be config - `[defaults] cols/rows` and a per-session override - and
/// that was the wrong place for it twice over: whatever is in a file is wrong the
/// moment the game window is a different shape, and nothing was ever going to
/// keep the two in step by hand. The terminal window measures itself in cells and
/// sends a `resize` (see `TerminalWindow.NegotiateSize`), so the window is
/// authoritative and this is only what the pane wears until it is looked at -
/// which matters, because an agent that starts, prints and is never opened still
/// has to have wrapped its output at something sane.
const BOOT_COLS: u16 = 120;
const BOOT_ROWS: u16 = 34;

fn disk_mtime(path: &std::path::Path) -> Option<SystemTime> {
    std::fs::metadata(path).ok()?.modified().ok()
}

fn now_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0)
}

fn hash_lines(lines: &[String]) -> u64 {
    let mut h = std::collections::hash_map::DefaultHasher::new();
    lines.hash(&mut h);
    h.finish()
}

fn compile_rules(cfg: &Config) -> Vec<(State, Regex)> {
    cfg.state_rules
        .iter()
        .filter_map(|r| {
            let state = match r.state.as_str() {
                "waiting" => State::Waiting,
                "working" => State::Working,
                "idle" => State::Idle,
                other => {
                    tracing::warn!("state_rule has unknown state {other:?}, ignoring");
                    return None;
                }
            };
            match Regex::new(&r.pattern) {
                Ok(re) => Some((state, re)),
                Err(e) => {
                    tracing::warn!("bad state_rule pattern {:?}: {e}", r.pattern);
                    None
                }
            }
        })
        .collect()
}

/// A session name is also a tmux target, where ':' and '.' select a window and a
/// pane, and it is also a colonist's name in the mod.
fn check_name(name: &str) -> Result<()> {
    if name.is_empty() || name.contains(|c: char| c.is_whitespace() || c == ':' || c == '.') {
        bail!("session name must be non-empty and free of whitespace, ':' and '.'");
    }
    Ok(())
}

/// A project name is only ever a key in config.toml and a label in the GUI, so
/// the bar is lower than a session's - but it is what a session points at, and
/// a blank one would point at all of them.
fn check_project(p: &ProjectCfg) -> Result<()> {
    if p.name.trim().is_empty() {
        bail!("project name must not be empty");
    }
    if p.dir.trim().is_empty() {
        bail!("project {} needs a directory", p.name);
    }
    for name in &p.presets {
        if crate::sandbox::preset(name).is_none() {
            bail!("unknown sandbox preset: {name}");
        }
    }
    Ok(())
}

/// Every agent works somewhere. Enforced on the way in rather than on start, so
/// the dialog that made the mistake is the thing that says so.
fn check_belongs(cfg: &Config, s: &SessionCfg) -> Result<()> {
    if s.project.trim().is_empty() {
        bail!("session {} must belong to a project", s.name);
    }
    if cfg.project(&s.project).is_none() {
        bail!("no such project: {}", s.project);
    }
    Ok(())
}

impl Manager {
    pub async fn new(cfg: Config, cfg_path: PathBuf) -> Arc<Self> {
        let (events, _) = broadcast::channel(256);
        let mtime = disk_mtime(&cfg_path);
        let m = Arc::new(Self {
            tmux: Tmux::new(cfg.daemon.tmux_socket.clone(), cfg.daemon.history_limit),
            cfg_path,
            rules: RwLock::new(compile_rules(&cfg)),
            live: RwLock::new(HashMap::new()),
            cfg: RwLock::new(cfg),
            cfg_mtime: Mutex::new(mtime),
            cfg_checked: Mutex::new(0),
            usage: RwLock::new(crate::usage::Snapshot::default()),
            clients: AtomicUsize::new(0),
            clients_since: AtomicU64::new(0),
            events,
        });
        // Before anything else touches tmux: whoever forks the server decides
        // which cgroup it dies with.
        m.tmux.ensure_server().await;
        m.sync_from_config().await;
        m
    }

    /// Writes config out and remembers the mtime we left behind, so our own write
    /// doesn't read back as somebody else's edit.
    fn save_cfg(&self, cfg: &Config) -> Result<()> {
        cfg.save(&self.cfg_path)?;
        *self.cfg_mtime.lock().unwrap() = disk_mtime(&self.cfg_path);
        Ok(())
    }

    /// Picks up an edit made to config.toml outside the daemon.
    ///
    /// The file belongs to the user as much as to us - an agent working on this
    /// repo edits it by hand - but slopd only read it at startup and rewrote it
    /// whole on every API write, so a hand edit was both invisible and doomed. Any
    /// mtime we didn't cause means re-read, and every mutating call goes through
    /// here first so the write it is about to make lands on top of that edit
    /// rather than over it.
    ///
    /// A file that doesn't parse is left alone with a warning and the in-memory
    /// config keeps serving; the mtime is recorded either way, so a broken file
    /// complains once rather than every two seconds.
    pub async fn reload_if_changed(self: &Arc<Self>) -> bool {
        let disk = disk_mtime(&self.cfg_path);
        {
            let mut seen = self.cfg_mtime.lock().unwrap();
            if *seen == disk {
                return false;
            }
            *seen = disk;
        }

        let text = match std::fs::read_to_string(&self.cfg_path) {
            Ok(t) => t,
            Err(e) => {
                tracing::warn!("config changed on disk but is unreadable: {e:#}");
                return false;
            }
        };
        let new = match Config::parse(&text) {
            Ok(c) => c,
            Err(e) => {
                tracing::warn!("config changed on disk but does not parse, keeping the old one: {e:#}");
                return false;
            }
        };

        tracing::info!("config changed on disk, reloading");
        *self.rules.write().await = compile_rules(&new);
        *self.cfg.write().await = new;
        self.sync_from_config().await;
        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });
        self.announce_projects().await;
        true
    }

    /// Throttled `reload_if_changed`, for the tick to call.
    async fn reload_if_due(self: &Arc<Self>) {
        {
            let mut last = self.cfg_checked.lock().unwrap();
            let now = now_ms();
            if now.saturating_sub(*last) < CFG_CHECK_MS {
                return;
            }
            *last = now;
        }
        self.reload_if_changed().await;
    }

    /// Relaunches the game, for the in-game "save and restart" - the mod saves,
    /// asks for this, and quits. The delay is the mod's estimate of how long its
    /// own shutdown takes: slopd has no handle on the game process, so the two
    /// only overlap by wall clock.
    ///
    /// Detached deliberately: the game must outlive the daemon that started it,
    /// including the redeploy that is usually the reason for the restart.
    pub async fn restart_game(&self, delay_ms: u64) -> Result<()> {
        let cmd = self.config().await.daemon.game_cmd.trim().to_string();
        if cmd.is_empty() {
            bail!("daemon.game_cmd is not set, so there is nothing to launch");
        }
        let argv = crate::sandbox::shell_split(&cmd);
        let Some((exe, args)) = argv.split_first() else {
            bail!("daemon.game_cmd is empty after splitting");
        };
        let exe = exe.clone();
        let args: Vec<String> = args.to_vec();

        // Ask whoever is in there to go, because the daemon cannot save a colony
        // and the game can. A mod that called this endpoint itself is already
        // shutting down and will ignore its own echo; an agent running `make
        // redeploy` has told nobody, and this is what tells them.
        let _ = self.events.send(Event::Quit);
        let watch = self.config().await.daemon.game_cmd;

        tokio::spawn(async move {
            tokio::time::sleep(Duration::from_millis(delay_ms.min(60_000))).await;

            // And then wait for it to actually be gone. Launching on a timer is
            // how a redeploy ends up with two RimWorlds fighting over one save:
            // the delay is the caller's estimate of its own shutdown, and a
            // colony that takes longer to write than expected is exactly the
            // case where that estimate is wrong.
            for _ in 0..QUIT_WAIT_TICKS {
                if !crate::game::status(&watch, 0, None).running {
                    match crate::game::launch(&exe, &args) {
                        Ok(()) => tracing::info!("relaunched the game: {exe}"),
                        Err(e) => tracing::error!("relaunching the game: {e:#}"),
                    }
                    return;
                }
                tokio::time::sleep(Duration::from_millis(500)).await;
            }
            tracing::error!(
                "the game is still running after being asked to quit; not starting a second one"
            );
        });
        Ok(())
    }

    /// What the daemon can see of the game, for the benefit of anything that
    /// cannot see it at all. See `game.rs`.
    pub async fn game(&self) -> crate::game::Status {
        let cmd = self.config().await.daemon.game_cmd;
        let clients = self.clients.load(Ordering::Relaxed);
        let since = self.clients_since.load(Ordering::Relaxed);
        let up = (clients > 0 && since > 0).then(|| now_ms().saturating_sub(since) / 1000);
        crate::game::status(&cmd, clients, up)
    }

    /// Counts a websocket client for as long as the guard lives.
    pub fn client_joined(self: &Arc<Self>) -> ClientGuard {
        if self.clients.fetch_add(1, Ordering::Relaxed) == 0 {
            self.clients_since.store(now_ms(), Ordering::Relaxed);
        }
        ClientGuard(self.clone())
    }

    pub async fn config(&self) -> Config {
        self.cfg.read().await.clone()
    }

    pub async fn usage(&self) -> crate::usage::Snapshot {
        self.usage.read().await.clone()
    }

    /// Stores a fresh snapshot and tells the clients, but only if it says
    /// something new: the poll runs every minute forever and an unchanged
    /// percentage is not an event.
    pub async fn set_usage(&self, snap: crate::usage::Snapshot) {
        {
            let mut cur = self.usage.write().await;
            // fetched_ms moves on every successful poll, so compare the parts a
            // reader would notice rather than the whole struct.
            if cur.ok == snap.ok
                && cur.error == snap.error
                && cur.plan == snap.plan
                && cur.windows == snap.windows
            {
                *cur = snap;
                return;
            }
            *cur = snap.clone();
        }
        let _ = self.events.send(Event::Usage { usage: snap });
    }

    /// Reconciles the live table with config: adds new entries, drops removed
    /// ones, autostarts what asks for it.
    pub async fn sync_from_config(self: &Arc<Self>) {
        let cfg = self.config().await;
        let mut live = self.live.write().await;

        live.retain(|name, _| cfg.session(name).is_some());

        for s in &cfg.sessions {
            live.entry(s.name.clone())
                .and_modify(|l| l.cfg = s.clone())
                .or_insert(Live {
                    cfg: s.clone(),
                    state: State::Down,
                    seq: 0,
                    hash: 0,
                    last_change: 0,
                    cols: BOOT_COLS,
                    rows: BOOT_ROWS,
                    screen: None,
                    emu: None,
                    reader: None,
                });
        }
        drop(live);

        for s in cfg.sessions.iter().filter(|s| s.autostart) {
            if !self.tmux.exists(&s.name).await {
                if let Err(e) = self.start(&s.name).await {
                    tracing::error!("autostart {}: {e:#}", s.name);
                }
            }
        }

        // Attach a control reader to any session that's already running (e.g. one
        // that survived a daemon restart). Idempotent: skips sessions we already
        // hold an emulator for.
        let mut orphans: Vec<String> = Vec::new();
        for name in self.tmux.list().await {
            if !self.live.read().await.contains_key(&name) {
                orphans.push(name);
                continue;
            }
            if self.spawn_reader(&name).await {
                // Adopted rather than started by us, so the app on the other end
                // has no idea it has a new reader. Ask it to repaint, off the
                // critical path - each nudge sleeps.
                let m = self.clone();
                let name = name.clone();
                tokio::spawn(async move {
                    let (cols, rows) = match m.live.read().await.get(&name) {
                        Some(l) => (l.cols, l.rows),
                        None => return,
                    };
                    if let Err(e) = m.tmux.nudge_redraw(&name, cols, rows).await {
                        tracing::debug!("redraw nudge {name}: {e:#}");
                    }
                });
            }
        }

        // Running under our socket but absent from config: someone renamed or
        // deleted the entry while the agent kept working. Left alone - killing it
        // would be worse - but said out loud, because it is invisible in-game.
        if !orphans.is_empty() {
            tracing::warn!(
                "tmux sessions with no config entry, not shown in-game: {}",
                orphans.join(", ")
            );
        }
    }

    pub async fn start(self: &Arc<Self>, name: &str) -> Result<()> {
        let cfg = self.config().await;
        let s = cfg
            .session(name)
            .ok_or_else(|| anyhow!("no such session: {name}"))?
            .clone();

        let p = cfg.project_of(&s).cloned().ok_or_else(|| {
            if s.project.is_empty() {
                anyhow!("session {name} belongs to no project")
            } else {
                anyhow!("session {name} belongs to project {}, which does not exist", s.project)
            }
        })?;

        if self.tmux.exists(name).await {
            bail!("session {name} is already running");
        }
        let dir = expand(&p.dir);
        if !std::path::Path::new(&dir).is_dir() {
            bail!("{dir} is not a directory");
        }

        // The size the live entry is already carrying: a restart of a session whose
        // terminal is open should come back the shape the window asked for, not the
        // shape a fresh one starts at.
        let (cols, rows) = match self.live.read().await.get(name) {
            Some(l) => (l.cols, l.rows),
            None => (BOOT_COLS, BOOT_ROWS),
        };
        let argv = build_argv(&cfg, &s, &p);
        tracing::info!("starting {name}: {}", argv.join(" "));
        self.tmux.spawn(name, &dir, cols, rows, &argv).await?;
        self.spawn_reader(name).await;
        Ok(())
    }

    pub async fn stop(self: &Arc<Self>, name: &str) -> Result<()> {
        if self.tmux.exists(name).await {
            self.tmux.kill(name).await?;
        }
        if let Some(l) = self.live.write().await.get_mut(name) {
            l.state = State::Down;
            l.screen = None;
            l.emu = None;
            if let Some(h) = l.reader.take() {
                h.abort();
            }
        }
        Ok(())
    }

    pub async fn restart(self: &Arc<Self>, name: &str) -> Result<()> {
        self.stop(name).await?;
        self.start(name).await
    }

    // ------------------------------------------------------------- projects

    pub async fn projects(&self) -> Vec<ProjectCfg> {
        self.cfg.read().await.projects.clone()
    }

    /// Tells the clients what the projects are now. Cheap enough to send on any
    /// change: there are a handful of them and they move when a person edits
    /// one, not on a tick.
    async fn announce_projects(&self) {
        let _ = self.events.send(Event::Projects {
            projects: self.projects().await,
        });
    }

    pub async fn add_project(self: &Arc<Self>, p: ProjectCfg) -> Result<()> {
        self.reload_if_changed().await;
        check_project(&p)?;
        let mut cfg = self.cfg.write().await;
        if cfg.project(&p.name).is_some() {
            bail!("project {} already exists", p.name);
        }
        cfg.projects.push(p);
        self.save_cfg(&cfg)?;
        drop(cfg);
        self.announce_projects().await;
        Ok(())
    }

    /// Writes a project back. A changed name is a rename, and every session
    /// pointing at the old one is carried over in the same write - a session
    /// left naming a project that no longer exists is one that will not start,
    /// and nothing in the GUI would have said why.
    pub async fn update_project(self: &Arc<Self>, name: &str, p: ProjectCfg) -> Result<()> {
        self.reload_if_changed().await;
        check_project(&p)?;

        let mut cfg = self.cfg.write().await;
        let idx = cfg
            .projects
            .iter()
            .position(|x| x.name == name)
            .ok_or_else(|| anyhow!("no such project: {name}"))?;
        if p.name != name && cfg.project(&p.name).is_some() {
            bail!("project {} already exists", p.name);
        }

        let renamed = p.name.clone();
        cfg.projects[idx] = p;
        if renamed != name {
            for s in cfg.sessions.iter_mut().filter(|s| s.project == name) {
                s.project = renamed.clone();
            }
        }
        self.save_cfg(&cfg)?;
        drop(cfg);

        self.announce_projects().await;
        // The directory or the sandbox may have moved under a running agent,
        // which keeps running under the old one until it is restarted - but the
        // list has to say what the next start will use.
        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });
        Ok(())
    }

    /// Refused while anything still works there. The alternative is deleting the
    /// sessions with it, which is throwing away agents to tidy a list.
    pub async fn remove_project(self: &Arc<Self>, name: &str) -> Result<()> {
        self.reload_if_changed().await;
        let mut cfg = self.cfg.write().await;
        if cfg.project(name).is_none() {
            bail!("no such project: {name}");
        }
        let users: Vec<&str> = cfg
            .sessions
            .iter()
            .filter(|s| s.project == name)
            .map(|s| s.name.as_str())
            .collect();
        if !users.is_empty() {
            bail!(
                "project {name} still has agents in it: {}. Remove or move them first.",
                users.join(", ")
            );
        }
        cfg.projects.retain(|p| p.name != name);
        self.save_cfg(&cfg)?;
        drop(cfg);
        self.announce_projects().await;
        Ok(())
    }

    // ------------------------------------------------------------- sessions

    /// Adds a session to config, persists, and starts it if asked.
    pub async fn add(self: &Arc<Self>, s: SessionCfg) -> Result<()> {
        // Land on top of any hand edit rather than over it.
        self.reload_if_changed().await;
        let mut cfg = self.cfg.write().await;
        if cfg.session(&s.name).is_some() {
            bail!("session {} already exists", s.name);
        }
        check_name(&s.name)?;
        check_belongs(&cfg, &s)?;
        let autostart = s.autostart;
        let name = s.name.clone();
        cfg.sessions.push(s);
        self.save_cfg(&cfg)?;
        drop(cfg);

        self.sync_from_config().await;
        if autostart {
            self.start(&name).await.ok();
        }
        Ok(())
    }

    /// Writes a session's config back. A changed name is a rename: the agent keeps
    /// running under it, so tmux is renamed first - if it refuses, config.toml and
    /// reality stay in step.
    pub async fn update(self: &Arc<Self>, name: &str, s: SessionCfg) -> Result<()> {
        self.reload_if_changed().await;
        let renamed = s.name != name;
        if renamed {
            check_name(&s.name)?;
            let cfg = self.cfg.read().await;
            if cfg.session(name).is_none() {
                bail!("no such session: {name}");
            }
            if cfg.session(&s.name).is_some() {
                bail!("session {} already exists", s.name);
            }
            drop(cfg);
            if self.tmux.exists(name).await {
                self.tmux.rename(name, &s.name).await?;
            }
        }

        let mut cfg = self.cfg.write().await;
        check_belongs(&cfg, &s)?;
        let idx = cfg
            .sessions
            .iter()
            .position(|x| x.name == name)
            .ok_or_else(|| anyhow!("no such session: {name}"))?;
        cfg.sessions[idx] = s.clone();
        self.save_cfg(&cfg)?;
        drop(cfg);

        if renamed {
            self.readopt(name, &s.name).await;
        }
        self.sync_from_config().await;
        Ok(())
    }

    /// Carries a renamed session's live state to its new key. The control reader
    /// is pinned to the name it attached with, so it is dropped here and, if the
    /// session was running, re-attached under the new one - which also re-seeds
    /// the emulator from a capture, leaving the screen where the agent left it.
    async fn readopt(self: &Arc<Self>, old: &str, new: &str) {
        let running = {
            let mut live = self.live.write().await;
            let mut l = match live.remove(old) {
                Some(l) => l,
                None => return,
            };
            if let Some(h) = l.reader.take() {
                h.abort();
            }
            let running = l.emu.take().is_some();
            l.cfg.name = new.to_string();
            live.insert(new.to_string(), l);
            running
        };
        if running {
            self.spawn_reader(new).await;
        }
    }

    pub async fn remove(self: &Arc<Self>, name: &str) -> Result<()> {
        self.reload_if_changed().await;
        self.stop(name).await.ok();
        let mut cfg = self.cfg.write().await;
        cfg.sessions.retain(|s| s.name != name);
        self.save_cfg(&cfg)?;
        drop(cfg);
        self.sync_from_config().await;
        Ok(())
    }

    /// Writes back whole config sections from the settings GUI. Sessions and state
    /// rules are addressed by their own endpoints and are never touched here.
    /// `bind`, `tmux_socket` and `poll_ms` are read once at startup, so those land
    /// in the file now and take hold when slopd restarts.
    pub async fn update_sections(
        self: &Arc<Self>,
        daemon: Option<Daemon>,
        defaults: Option<Defaults>,
        sandbox: Option<Sandbox>,
    ) -> Result<()> {
        self.reload_if_changed().await;

        if let Some(d) = &daemon {
            d.bind
                .parse::<std::net::SocketAddr>()
                .with_context(|| format!("bad bind address {:?}", d.bind))?;
            if d.tmux_socket.trim().is_empty() {
                bail!("tmux socket name must not be empty");
            }
        }
        if let Some(d) = &defaults {
            if d.agent.trim().is_empty() {
                bail!("default agent command must not be empty");
            }
        }

        let mut cfg = self.cfg.write().await;
        if let Some(d) = daemon {
            cfg.daemon = d;
        }
        if let Some(d) = defaults {
            cfg.defaults = d;
        }
        if let Some(s) = sandbox {
            cfg.sandbox = s;
        }
        self.save_cfg(&cfg)?;
        drop(cfg);

        self.sync_from_config().await;
        Ok(())
    }

    pub async fn replace_config(self: &Arc<Self>, text: &str) -> Result<()> {
        let new = Config::parse(text)?;
        self.save_cfg(&new)?;
        *self.rules.write().await = compile_rules(&new);
        *self.cfg.write().await = new;
        self.sync_from_config().await;
        self.announce_projects().await;
        Ok(())
    }

    pub async fn views(&self) -> Vec<SessionView> {
        let cfg = self.config().await;
        let live = self.live.read().await;
        let mut out: Vec<SessionView> = live
            .values()
            .map(|l| {
                let p = cfg.project_of(&l.cfg);
                SessionView {
                    name: l.cfg.name.clone(),
                    project: l.cfg.project.clone(),
                    dir: p.map(|p| p.dir.clone()).unwrap_or_default(),
                    kind: l.cfg.kind.as_str().into(),
                    agent: cfg.command_of(&l.cfg),
                    state: l.state,
                    alive: l.state != State::Down,
                    cols: l.cols,
                    rows: l.rows,
                    net: p.map(|p| p.net).unwrap_or(true),
                    sandbox: p.map(|p| p.sandbox).unwrap_or(true),
                    autostart: l.cfg.autostart,
                    last_change: l.last_change,
                    seq: l.seq,
                }
            })
            .collect();
        out.sort_by(|a, b| a.name.cmp(&b.name));
        out
    }

    pub async fn screen(&self, name: &str) -> Option<ScreenView> {
        self.live.read().await.get(name)?.screen.clone()
    }

    pub async fn send_keys(&self, name: &str, keys: Vec<String>, literal: bool) -> Result<()> {
        if !self.tmux.exists(name).await {
            bail!("session {name} is not running");
        }
        self.tmux.send_keys(name, &keys, literal).await
    }

    /// Encodes a mouse event to the app's current mouse protocol and writes it to
    /// the pane. A no-op if the app isn't in a mouse mode (the mod only forwards
    /// when it saw `app_mouse`, but re-check here off the authoritative emulator).
    pub async fn send_mouse(&self, name: &str, ev: MouseInput) -> Result<()> {
        if !self.tmux.exists(name).await {
            bail!("session {name} is not running");
        }
        let bytes = {
            let live = self.live.read().await;
            live.get(name)
                .and_then(|l| l.emu.clone())
                .and_then(|e| e.lock().ok().and_then(|g| g.mouse_report(&ev)))
        };
        if let Some(b) = bytes {
            self.tmux.send_bytes(name, &b).await?;
        }
        Ok(())
    }

    /// Pastes text into the pane. If the app enabled bracketed paste, the text is
    /// wrapped in `\x1b[200~..\x1b[201~` and any end-marker inside the content is
    /// stripped so a paste can't forge the terminator (paste-injection). Sent as
    /// raw bytes so escapes survive verbatim.
    pub async fn paste(&self, name: &str, text: &str) -> Result<()> {
        if !self.tmux.exists(name).await {
            bail!("session {name} is not running");
        }
        let bracketed = {
            let live = self.live.read().await;
            live.get(name)
                .and_then(|l| l.emu.clone())
                .map(|e| e.lock().map(|g| g.bracketed_paste()).unwrap_or(false))
                .unwrap_or(false)
        };

        let mut bytes = Vec::new();
        if bracketed {
            bytes.extend_from_slice(b"\x1b[200~");
            // Drop any embedded terminator so pasted content can't end the paste early.
            bytes.extend_from_slice(text.replace("\x1b[201~", "").as_bytes());
            bytes.extend_from_slice(b"\x1b[201~");
        } else {
            bytes.extend_from_slice(text.as_bytes());
        }
        self.tmux.send_bytes(name, &bytes).await
    }

    pub async fn resize(&self, name: &str, cols: u16, rows: u16) -> Result<()> {
        let cols = cols.clamp(20, 500);
        let rows = rows.clamp(5, 200);
        {
            let mut live = self.live.write().await;
            let l = live
                .get_mut(name)
                .ok_or_else(|| anyhow!("no such session: {name}"))?;
            if l.cols == cols && l.rows == rows {
                return Ok(());
            }
            l.cols = cols;
            l.rows = rows;
        }
        if self.tmux.exists(name).await {
            self.tmux.resize(name, cols, rows).await?;
        }
        if let Some(emu) = self.live.read().await.get(name).and_then(|l| l.emu.clone()) {
            if let Ok(mut e) = emu.lock() {
                e.resize(cols, rows);
            }
        }
        Ok(())
    }

    async fn classify(&self, changed: bool, last_change: u64, text: &str) -> State {
        for (state, re) in self.rules.read().await.iter() {
            if re.is_match(text) {
                return *state;
            }
        }
        // No rule hit: a pane that moved recently is still doing something; once
        // it has been quiet for IDLE_MS it has gone idle.
        if changed || now_ms().saturating_sub(last_change) < IDLE_MS {
            State::Working
        } else {
            State::Idle
        }
    }

    /// A cheap in-memory tick over every live session: re-runs the state rules so
    /// a session that fell quiet decays working -> idle. No tmux spawns and no
    /// captures; screen changes arrive event-driven from the control readers.
    pub async fn retick(self: &Arc<Self>) {
        self.reload_if_due().await;

        // Snapshot outside the lock so classify()'s await doesn't hold it.
        let snapshot: Vec<(String, u64, String)> = {
            let live = self.live.read().await;
            live.iter()
                .filter(|(_, l)| l.state != State::Down)
                .filter_map(|(n, l)| {
                    let s = l.screen.as_ref()?;
                    Some((n.clone(), l.last_change, strip_sgr(&s.lines.join("\n"))))
                })
                .collect()
        };

        let mut dirty_list = false;
        for (name, last_change, plain) in snapshot {
            let state = self.classify(false, last_change, &plain).await;
            let mut live = self.live.write().await;
            if let Some(l) = live.get_mut(&name) {
                if l.state != state {
                    l.state = state;
                    dirty_list = true;
                }
            }
        }

        if dirty_list {
            let _ = self.events.send(Event::Sessions {
                sessions: self.views().await,
            });
        }
    }

    /// Attaches a control-mode reader to a running session and wires up its
    /// emulator. Idempotent: does nothing if we already hold an emulator for the
    /// session. Reseeds the fresh emulator from the current pane so an attach
    /// mid-session (daemon restart) shows real content, not a blank grid.
    async fn spawn_reader(self: &Arc<Self>, name: &str) -> bool {
        let (cols, rows, has_emu) = {
            let live = self.live.read().await;
            match live.get(name) {
                Some(l) => (l.cols, l.rows, l.emu.is_some()),
                None => return false,
            }
        };
        if has_emu {
            return false;
        }

        let mut e = SessionEmu::new(cols, rows);
        // Seeded with tmux's scrollback, not just the visible pane: history is the
        // half of a reattached session that used to come back empty, and the lines
        // that scroll off the top land in the emulator's own scrollback, where the
        // wheel can reach them again.
        let history = self.config().await.daemon.history_limit;
        if let Ok(cap) = self.tmux.capture(name, history).await {
            let mut seed = String::from("\x1b[2J\x1b[H\x1b[0m");
            seed.push_str(&cap.lines.join("\r\n"));
            seed.push_str(&format!("\x1b[{};{}H", cap.cy + 1, cap.cx + 1));
            e.feed(seed.as_bytes());
        }
        let emu = Arc::new(Mutex::new(e));

        let handle = {
            let m = self.clone();
            let name = name.to_string();
            let emu = emu.clone();
            tokio::spawn(async move { m.run_control(name, emu).await })
        };

        {
            let mut live = self.live.write().await;
            match live.get_mut(name) {
                Some(l) => {
                    l.emu = Some(emu.clone());
                    l.reader = Some(handle);
                }
                None => {
                    handle.abort();
                    return false;
                }
            }
        }

        // Push the seeded frame so fresh subscribers see content before any output.
        self.render_and_broadcast(name, &emu).await;
        true
    }

    /// The control-reader task: pumps `%output` bytes into the emulator and, on a
    /// coalescing tick, renders + broadcasts. Ends (marking the session down) when
    /// the session emits `%exit` or the control client's stdout closes.
    async fn run_control(self: Arc<Self>, name: String, emu: Arc<Mutex<SessionEmu>>) {
        let (cols, rows) = match self.live.read().await.get(&name) {
            Some(l) => (l.cols, l.rows),
            None => {
                self.mark_down(&name).await;
                return;
            }
        };
        // Held for the task's life: dropping it kills the control client (detach).
        let (_child, master) = match self.tmux.control_attach(&name, cols, rows) {
            Ok(c) => c,
            Err(e) => {
                tracing::error!("control attach {name}: {e:#}");
                self.mark_down(&name).await;
                return;
            }
        };

        // The master is a blocking pty fd; bridge it to async with a reader thread
        // that ships `%`-notification lines over a channel. Lines are raw bytes,
        // not `String`: tmux writes UTF-8 literally in `%output` and can split a
        // multibyte char across chunks, so a line need not be valid UTF-8. The
        // thread ends (and the channel closes) when the client dies and the master
        // reads EOF/EIO.
        let (tx, mut rx) = tokio::sync::mpsc::unbounded_channel::<Vec<u8>>();
        std::thread::spawn(move || {
            use std::io::BufRead;
            let mut reader = std::io::BufReader::new(master);
            let mut line: Vec<u8> = Vec::new();
            loop {
                line.clear();
                match reader.read_until(b'\n', &mut line) {
                    Ok(0) => break, // EOF
                    Ok(_) => {
                        // Drop the LF and the CR that pty ONLCR added ahead of it;
                        // tmux octal-escapes real control bytes, so a trailing CR
                        // here is always that artifact, never payload.
                        if line.last() == Some(&b'\n') {
                            line.pop();
                        }
                        if line.last() == Some(&b'\r') {
                            line.pop();
                        }
                        if tx.send(line.clone()).is_err() {
                            break;
                        }
                    }
                    Err(_) => break,
                }
            }
        });

        // Coalesce bursts of output into ~60fps renders instead of one per line.
        let mut flush = interval(Duration::from_millis(8));
        flush.set_missed_tick_behavior(MissedTickBehavior::Delay);
        let mut dirty = false;

        loop {
            tokio::select! {
                line = rx.recv() => match line {
                    Some(l) => {
                        if let Some(bytes) = parse_output(&l) {
                            let replies = if let Ok(mut e) = emu.lock() {
                                e.feed(&bytes);
                                e.take_replies()
                            } else {
                                Vec::new()
                            };
                            // Answer cursor-position/device queries the app sent, so
                            // TUIs like Claude Code place their cursor correctly.
                            if !replies.is_empty() {
                                if let Err(e) = self.tmux.send_bytes(&name, &replies).await {
                                    tracing::debug!("pty reply write {name}: {e:#}");
                                }
                            }
                            dirty = true;
                        } else if l.starts_with(b"%exit") {
                            break;
                        }
                    }
                    // Reader thread ended: the client is gone.
                    None => break,
                },
                _ = flush.tick() => {
                    if dirty {
                        dirty = false;
                        self.render_and_broadcast(&name, &emu).await;
                    }
                }
            }
        }

        self.mark_down(&name).await;
    }

    /// Renders the emulator's live frame and applies it to the session's live
    /// state, broadcasting a screen (and, on a state move, a session list) event.
    async fn render_and_broadcast(&self, name: &str, emu: &Mutex<SessionEmu>) {
        let frame = match emu.lock() {
            Ok(e) => e.render(),
            Err(_) => return,
        };
        self.apply_frame(name, frame).await;
    }

    /// Diffs a freshly-rendered frame against the stored one, updates live state,
    /// and broadcasts what changed. Mirrors the old poll_session bookkeeping but
    /// off the emulator instead of a capture.
    async fn apply_frame(&self, name: &str, frame: Frame) {
        let hash = hash_lines(&frame.lines);
        let (prev_hash, prev_state, prev_change, seq, prev_cursor, prev_meta, cols, rows) = {
            let live = self.live.read().await;
            let Some(l) = live.get(name) else { return };
            let cur = l.screen.as_ref().map(|s| (s.cx, s.cy)).unwrap_or((0, 0));
            let meta = l
                .screen
                .as_ref()
                .map(|s| (s.cursor_shape, s.cursor_blink, s.app_mouse, s.alt_screen))
                .unwrap_or((0, false, false, false));
            (
                l.hash,
                l.state,
                l.last_change,
                l.seq,
                cur,
                meta,
                l.cols,
                l.rows,
            )
        };

        let meta = (
            frame.cursor_shape,
            frame.cursor_blink,
            frame.app_mouse,
            frame.alt_screen,
        );
        let changed =
            hash != prev_hash || (frame.cx, frame.cy) != prev_cursor || meta != prev_meta;
        let plain = strip_sgr(&frame.lines.join("\n"));
        let state = self.classify(changed, prev_change, &plain).await;

        if !changed && state == prev_state {
            return;
        }

        let view = ScreenView::from_frame(name, seq + 1, cols, rows, 0, frame);

        let mut dirty_list = false;
        {
            let mut live = self.live.write().await;
            let Some(l) = live.get_mut(name) else { return };
            l.hash = hash;
            l.seq += 1;
            if changed {
                l.last_change = now_ms();
            }
            if l.state != state {
                l.state = state;
                dirty_list = true;
            }
            l.screen = Some(view.clone());
        }

        if changed {
            let _ = self.events.send(Event::Screen { screen: view });
        }
        if dirty_list {
            let _ = self.events.send(Event::Sessions {
                sessions: self.views().await,
            });
        }
    }

    /// Marks a session down and tears down its emulator + reader. Broadcasts a
    /// session list only when the state actually moved.
    async fn mark_down(&self, name: &str) {
        let mut changed = false;
        {
            let mut live = self.live.write().await;
            if let Some(l) = live.get_mut(name) {
                if l.state != State::Down {
                    l.state = State::Down;
                    changed = true;
                }
                l.screen = None;
                l.emu = None;
                if let Some(h) = l.reader.take() {
                    h.abort();
                }
            }
        }
        if changed {
            let _ = self.events.send(Event::Sessions {
                sessions: self.views().await,
            });
        }
    }

    /// A one-off frame scrolled `off` lines into scrollback for a wheel request.
    /// Returned to the caller only, never broadcast, so it can't clobber the live
    /// view. `off == 0` (or beyond history) returns the live frame.
    pub async fn scroll_capture(&self, name: &str, off: u16) -> Option<ScreenView> {
        if off == 0 {
            return self.screen(name).await;
        }
        let (emu, seq, cols, rows) = {
            let live = self.live.read().await;
            let l = live.get(name)?;
            (l.emu.clone()?, l.seq, l.cols, l.rows)
        };
        let (frame, achieved) = {
            let mut e = emu.lock().ok()?;
            e.scroll_snapshot(off)
        };
        if achieved == 0 {
            return self.screen(name).await;
        }
        Some(ScreenView::from_frame(name, seq, cols, rows, achieved, frame))
    }
}

/// Drops CSI/OSC sequences so state rules match against plain text.
pub fn strip_sgr(s: &str) -> String {
    let b = s.as_bytes();
    let mut out = String::with_capacity(s.len());
    let mut i = 0;
    while i < b.len() {
        if b[i] == 0x1b && i + 1 < b.len() {
            match b[i + 1] {
                b'[' => {
                    i += 2;
                    while i < b.len() && !(0x40..=0x7e).contains(&b[i]) {
                        i += 1;
                    }
                    i += 1;
                }
                b']' => {
                    i += 2;
                    while i < b.len() && b[i] != 0x07 && b[i] != 0x1b {
                        i += 1;
                    }
                    i += 1;
                }
                _ => i += 2,
            }
            continue;
        }
        // Walk by char, not byte, so multibyte text survives.
        let ch_len = utf8_len(b[i]);
        if let Ok(ch) = std::str::from_utf8(&b[i..(i + ch_len).min(b.len())]) {
            out.push_str(ch);
        }
        i += ch_len;
    }
    out
}

fn utf8_len(first: u8) -> usize {
    match first {
        0x00..=0x7f => 1,
        0xc0..=0xdf => 2,
        0xe0..=0xef => 3,
        0xf0..=0xf7 => 4,
        _ => 1,
    }
}

#[cfg(test)]
mod tests {
    use super::{check_name, strip_sgr};

    #[test]
    fn rejects_names_tmux_would_read_as_targets() {
        assert!(check_name("claude").is_ok());
        assert!(check_name("claude-2").is_ok());
        assert!(check_name("").is_err());
        assert!(check_name("two words").is_err());
        assert!(check_name("win:pane").is_err());
        assert!(check_name("dot.ted").is_err());
    }

    #[test]
    fn strips_colour_and_keeps_text() {
        assert_eq!(strip_sgr("\x1b[31mred\x1b[0m done"), "red done");
        assert_eq!(strip_sgr("esc to interrupt"), "esc to interrupt");
        assert_eq!(strip_sgr("\x1b]0;title\x07body"), "body");
        assert_eq!(strip_sgr("\x1b[38;5;214m❯ 1.\x1b[m"), "❯ 1.");
    }
}
