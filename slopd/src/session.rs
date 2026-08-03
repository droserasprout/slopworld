use std::collections::HashMap;
use std::hash::{Hash, Hasher};
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, AtomicU64, AtomicUsize, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use anyhow::{anyhow, bail, Context, Result};
use regex::Regex;
use serde::{Deserialize, Serialize};
use tokio::sync::{broadcast, RwLock};
use tokio::task::JoinHandle;
use tokio::time::{interval, MissedTickBehavior};

use crate::config::{
    expand, Config, Daemon, Defaults, ProjectCfg, Sandbox, SessionCfg, ShortcutCfg, ShortcutLink,
};
use crate::emu::{parse_output, Frame, MouseInput, SessionEmu};
use crate::sandbox::build_argv;
use crate::tmux::Tmux;

/// Matches the mod's rule for an idle agent's colonist.
const IDLE_MS: u64 = 10_000;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum State {
    /// Never started, or the agent exited. The mod puts the colonist on the floor rather
    /// than killing it, so the same process can get the same body back.
    Down,
    /// Pane text is changing, or a rule says the agent is mid-turn.
    Working,
    /// Blocked on the human.
    Waiting,
    /// Alive, quiet, nothing to do.
    Idle,
}

#[derive(Debug, Clone, Serialize)]
pub struct SessionView {
    pub name: String,
    /// Repeated here so a session list reads without a join. Empty when the entry names a
    /// project that has gone.
    pub project: String,
    pub dir: String,
    /// The command preset it runs under, which is also what hands its sandbox that
    /// agent's own state directory. Empty when the entry states a command line instead.
    pub command: String,
    /// This agent's own answer to what that preset runs, sent back so an edit round-trips
    /// it rather than quietly clearing it.
    pub cmd: Option<String>,
    /// Sandbox presets this agent adds to its command's and its project's.
    pub sandbox: Vec<String>,
    /// As it will actually be exec'd, preset and defaults resolved.
    pub agent: String,
    pub state: State,
    pub alive: bool,
    /// The pane's size now, not a setting: the window owns this and sends `resize`.
    pub cols: u16,
    pub rows: u16,
    pub net: bool,
    pub env: Vec<String>,
    /// Sent so an edit round-trips it instead of quietly clearing it.
    pub autostart: bool,
    /// A shortcut's agent, or a session started by hand: it goes when its process does. On
    /// the wire because the GUI must not offer to edit an entry there is no entry for.
    pub ephemeral: bool,
    /// Unix millis of the last observed pane change.
    pub last_change: u64,
    pub seq: u64,
}

/// Both fields empty is "whatever the shortcut says".
#[derive(Debug, Clone, Default, Deserialize)]
pub struct RunWhere {
    /// One config knows: the ephemeral ones are gone by the time anything could name one.
    #[serde(default)]
    pub project: Option<String>,
    /// Beats `project` when both are given, being the more specific answer.
    #[serde(default)]
    pub temp: bool,
}

#[derive(Debug, Clone, Serialize)]
pub struct ScreenView {
    pub name: String,
    pub seq: u64,
    pub cols: u16,
    pub rows: u16,
    pub cx: u16,
    pub cy: u16,
    /// A scrolled frame is a one-off answer to a wheel event, never broadcast.
    #[serde(default)]
    pub off: u16,
    /// 0 = block, 1 = underline, 2 = beam.
    #[serde(default)]
    pub cursor_shape: u8,
    #[serde(default)]
    pub cursor_blink: bool,
    #[serde(default)]
    pub app_mouse: bool,
    /// So a drag belongs to the app rather than to the pane's own text selection.
    #[serde(default)]
    pub app_drag: bool,
    /// The app is on the alternate screen (no scrollback of its own).
    #[serde(default)]
    pub alt_screen: bool,
    /// What the app calls itself (OSC 0/2). Empty until it says.
    #[serde(default)]
    pub title: String,
    /// One entry per row, still carrying SGR escapes.
    pub lines: Vec<String>,
}

impl ScreenView {
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
            app_drag: frame.app_drag,
            alt_screen: frame.alt_screen,
            title: frame.title,
            lines: frame.lines,
        }
    }
}

#[derive(Debug, Clone, Serialize)]
#[serde(tag = "t", rename_all = "lowercase")]
pub enum Event {
    Sessions {
        sessions: Vec<SessionView>,
    },
    /// On connect too: the dialog that picks one has to draw before anybody has changed
    /// anything.
    Projects {
        projects: Vec<ProjectCfg>,
    },
    /// On connect too, same reason.
    Shortcuts {
        shortcuts: Vec<ShortcutCfg>,
    },
    Screen {
        screen: ScreenView,
    },
    /// Broadcast on a change only; the poll behind it runs on the wall clock.
    Usage {
        usage: crate::usage::Snapshot,
    },
    /// The only event that asks the mod for something. Nothing outside the game can save a
    /// colony: a killed RimWorld comes back at the last autosave.
    Quit,
}

/// `Timeout` is not a failure (see `READY_MS`); `Gone` is the only one that cancels typing.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Ready {
    Settled,
    Timeout,
    Gone,
}

struct Live {
    cfg: SessionCfg,
    /// Set for a shortcut's agent and for an adopted session: what makes the colonist leave
    /// for good rather than lie down.
    ephemeral: bool,
    state: State,
    seq: u64,
    hash: u64,
    last_change: u64,
    cols: u16,
    rows: u16,
    screen: Option<ScreenView>,
    /// Present only while a control reader is attached.
    emu: Option<Arc<Mutex<SessionEmu>>>,
    /// Aborted on stop/death.
    reader: Option<JoinHandle<()>>,
}

pub struct Manager {
    pub tmux: Tmux,
    pub cfg_path: PathBuf,
    cfg: RwLock<Config>,
    live: RwLock<HashMap<String, Live>>,
    /// Coined per errand run in `temp` mode and dropped with the agent that asked for it.
    /// Taken after `live` wherever both are held.
    temp: RwLock<HashMap<String, ProjectCfg>>,
    rules: RwLock<Vec<(State, Regex)>>,
    /// Anything else means someone edited the file behind us.
    cfg_mtime: Mutex<Option<SystemTime>>,
    /// The preset directory is watched on the same terms: the newest mtime in it, so a
    /// file added, edited or deleted all read as a change.
    presets_mtime: Mutex<Option<SystemTime>>,
    /// So the retick doesn't stat the file eighty times a second.
    cfg_checked: Mutex<u64>,
    /// Here rather than in the poller, so a client connecting between polls has something
    /// to draw.
    usage: RwLock<crate::usage::Snapshot>,
    /// Read by `/api/game` as evidence: a client is a game up far enough to have loaded the
    /// mod and talked to us.
    clients: AtomicUsize,
    clients_since: AtomicU64,
    pub events: broadcast::Sender<Event>,
}

/// A guard rather than a pair of calls: the pump has several ways out and every one has to
/// put the count back.
pub struct ClientGuard(Arc<Manager>);

impl Drop for ClientGuard {
    fn drop(&mut self) {
        self.0.clients.fetch_sub(1, Ordering::Relaxed);
    }
}

const CFG_CHECK_MS: u64 = 2_000;

/// Half-second polls; a minute is a long shutdown.
const QUIT_WAIT_TICKS: usize = 120;

/// What a pane wears until someone looks at it - the terminal window measures itself and
/// sends a `resize`. Still matters: an agent that starts, prints and is never opened has to
/// have wrapped at something.
const BOOT_COLS: u16 = 120;
const BOOT_ROWS: u16 = 34;

/// A settle rather than a pattern match: an agent's TUI and a shell prompt have nothing in
/// common to grep for, but both print and then stop. A slow first run looks the same from
/// outside, hence a ceiling in tens of seconds. The timeout does not cancel delivery - a pane
/// that never goes quiet is usually drawing a spinner.
const READY_MS: u64 = 30_000;
const SETTLE_MS: u64 = 750;
const ENTER_GAP_MS: u64 = 150;

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

/// A tmux target, where ':' and '.' select a window and a pane.
fn check_name(name: &str) -> Result<()> {
    if name.is_empty() || name.contains(|c: char| c.is_whitespace() || c == ':' || c == '.') {
        bail!("session name must be non-empty and free of whitespace, ':' and '.'");
    }
    Ok(())
}

/// A shortcut's name is free-form, but the session it lands is a tmux target.
/// Anything the two disagree about becomes a dash: "review diff" -> "review-diff".
fn slug(name: &str) -> String {
    let mut out = String::with_capacity(name.len());
    for ch in name.chars() {
        if ch.is_whitespace() || ch == ':' || ch == '.' || ch == '/' {
            if !out.ends_with('-') {
                out.push('-');
            }
        } else {
            out.push(ch);
        }
    }
    let out = out.trim_matches('-').to_string();
    if out.is_empty() {
        "shortcut".into()
    } else {
        out
    }
}

/// The live table is handed in because the caller already holds its write half: naming and
/// claiming are one decision, and an await between them is two runs of the same errand
/// picking the same name.
fn free_name(live: &HashMap<String, Live>, cfg: &Config, base: &str) -> String {
    let taken = |n: &str| live.contains_key(n) || cfg.sessions.iter().any(|s| s.name == n);

    if !taken(base) {
        return base.to_string();
    }
    (2..)
        .map(|i| format!("{base}-{i}"))
        .find(|n| !taken(n))
        .unwrap()
}

/// A blank name would be one every session points at.
fn check_project(p: &ProjectCfg) -> Result<()> {
    if p.name.trim().is_empty() {
        bail!("project name must not be empty");
    }
    if p.dir.trim().is_empty() {
        bail!("project {} needs a directory", p.name);
    }
    check_presets(&p.sandbox)?;
    Ok(())
}

/// Refused where it is typed, unlike a name read off the file, which is warned about and
/// dropped: the dialog that made the mistake is the one place it can be fixed.
fn check_presets(names: &[String]) -> Result<()> {
    let t = crate::presets::table();
    for name in names {
        if t.sandbox(name).is_none() {
            bail!("unknown sandbox preset: {name}");
        }
    }
    Ok(())
}

/// The project is checked whenever one is named, whatever the link says - in `temp` mode it
/// is the sandbox the fresh project copies. Only `project` mode insists on having one.
fn check_shortcut(cfg: &Config, sc: &ShortcutCfg) -> Result<()> {
    if sc.name.trim().is_empty() {
        bail!("shortcut name must not be empty");
    }
    if sc.link == ShortcutLink::Project && sc.project.trim().is_empty() {
        bail!("shortcut {} must belong to a project", sc.name);
    }
    if !sc.project.trim().is_empty() && cfg.project(&sc.project).is_none() {
        bail!("no such project: {}", sc.project);
    }
    if sc.text.trim().is_empty() {
        bail!("shortcut {} has nothing to send", sc.name);
    }
    Ok(())
}

/// On the way in, so everything downstream reads `dir` normally; a rename moves the project
/// to fresh ground.
fn settle(p: &mut ProjectCfg) {
    if p.temp {
        p.dir = crate::config::temp_dir(&slug(&p.name));
    }
}

/// Off the same base as the session's, so the agent and the place it works read as one.
fn free_project_name(cfg: &Config, temp: &HashMap<String, ProjectCfg>, base: &str) -> String {
    let taken = |n: &str| cfg.project(n).is_some() || temp.contains_key(n);
    if !taken(base) {
        return base.to_string();
    }
    (2..)
        .map(|i| format!("{base}-{i}"))
        .find(|n| !taken(n))
        .unwrap()
}

/// On the way in rather than at start, so the dialog that made the mistake says so.
fn check_belongs(cfg: &Config, s: &SessionCfg) -> Result<()> {
    if s.project.trim().is_empty() {
        bail!("session {} must belong to a project", s.name);
    }
    if cfg.project(&s.project).is_none() {
        bail!("no such project: {}", s.project);
    }
    check_presets(&s.sandbox)?;
    check_env(s)
}

fn check_env(s: &SessionCfg) -> Result<()> {
    for line in &s.env {
        let line = line.trim();
        if line.is_empty() || line.starts_with('#') {
            continue;
        }
        match line.split_once('=') {
            Some((k, _)) if !k.trim().is_empty() => {}
            _ => bail!("environment override {line:?} is not KEY=VALUE"),
        }
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
            temp: RwLock::new(HashMap::new()),
            cfg: RwLock::new(cfg),
            cfg_mtime: Mutex::new(mtime),
            presets_mtime: Mutex::new(crate::presets::dir_stamp()),
            cfg_checked: Mutex::new(0),
            usage: RwLock::new(crate::usage::Snapshot::default()),
            clients: AtomicUsize::new(0),
            clients_since: AtomicU64::new(0),
            events,
        });
        // Before anything else touches tmux: whoever forks the server decides which cgroup it
        // dies with.
        m.tmux.ensure_server().await;
        m.sync_from_config().await;
        m
    }

    /// Remembers the mtime we left behind, so our own write doesn't read back as
    /// somebody else's edit.
    fn save_cfg(&self, cfg: &Config) -> Result<()> {
        cfg.save(&self.cfg_path)?;
        *self.cfg_mtime.lock().unwrap() = disk_mtime(&self.cfg_path);
        Ok(())
    }

    /// Every mutating call goes through here first, so the write it is about to make lands on
    /// top of a hand edit rather than over it. A file that doesn't parse is left alone; the
    /// mtime is recorded either way, so it complains once rather than every two seconds.
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
                tracing::warn!(
                    "config changed on disk but does not parse, keeping the old one: {e:#}"
                );
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
        self.announce_shortcuts().await;
        true
    }

    async fn reload_if_due(self: &Arc<Self>) {
        {
            let mut last = self.cfg_checked.lock().unwrap();
            let now = now_ms();
            if now.saturating_sub(*last) < CFG_CHECK_MS {
                return;
            }
            *last = now;
        }
        self.reload_presets_if_changed().await;
        self.reload_if_changed().await;
    }

    /// A preset is what a session runs and what its sandbox is handed, so a table that
    /// moved changes every row the mod is drawing.
    pub async fn reload_presets_if_changed(self: &Arc<Self>) -> bool {
        let disk = crate::presets::dir_stamp();
        {
            let mut seen = self.presets_mtime.lock().unwrap();
            if *seen == disk {
                return false;
            }
            *seen = disk;
        }
        tracing::info!("presets changed on disk, reloading");
        crate::presets::reload();
        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });
        true
    }

    /// The delay is the mod's estimate of its own shutdown: slopd has no handle on the game
    /// process, so the two only overlap by wall clock. Detached, the game having to outlive
    /// the redeploy that is usually the reason for it.
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

        // Only the game can save a colony. A mod that called this endpoint is already
        // shutting down and ignores its own echo; an agent running `make redeploy` told nobody.
        let _ = self.events.send(Event::Quit);
        let watch = self.config().await.daemon.game_cmd;

        tokio::spawn(async move {
            tokio::time::sleep(Duration::from_millis(delay_ms.min(60_000))).await;

            // Waiting rather than launching on the timer: a colony that takes longer to write
            // than estimated is exactly the case where two RimWorlds fight over one save.
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

    /// For the benefit of anything that cannot see it at all. See `game.rs`.
    pub async fn game(&self) -> crate::game::Status {
        let cmd = self.config().await.daemon.game_cmd;
        let clients = self.clients.load(Ordering::Relaxed);
        let since = self.clients_since.load(Ordering::Relaxed);
        let up = (clients > 0 && since > 0).then(|| now_ms().saturating_sub(since) / 1000);
        crate::game::status(&cmd, clients, up)
    }

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

    /// Broadcast only on a change: the poll runs every minute forever.
    pub async fn set_usage(&self, snap: crate::usage::Snapshot) {
        {
            let mut cur = self.usage.write().await;
            // fetched_ms moves on every successful poll, so compare what a reader would notice
            // rather than the whole struct.
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

    pub async fn sync_from_config(self: &Arc<Self>) {
        let cfg = self.config().await;
        let mut live = self.live.write().await;

        // An ephemeral entry is its own record: the file only retires what the file owns.
        live.retain(|name, l| l.ephemeral || cfg.session(name).is_some());

        for s in &cfg.sessions {
            live.entry(s.name.clone())
                .and_modify(|l| l.cfg = s.clone())
                .or_insert(Live {
                    cfg: s.clone(),
                    ephemeral: false,
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

        // Catches anything that survived a daemon restart. Idempotent.
        let mut adopted = false;
        for name in self.tmux.list().await {
            if !self.live.read().await.contains_key(&name) {
                adopted |= self.adopt(&name).await;
            }
            // A session with no emulator is one we are picking up, so its shape is tmux's
            // answer and not ours: stating the boot size resized a running app down to it.
            let fresh = !self
                .live
                .read()
                .await
                .get(&name)
                .is_some_and(|l| l.emu.is_some());
            if fresh {
                if let Some((cols, rows)) = self.tmux.size(&name).await {
                    if let Some(l) = self.live.write().await.get_mut(&name) {
                        l.cols = cols;
                        l.rows = rows;
                    }
                }
            }
            if self.spawn_reader(&name).await {
                // Off the critical path: the nudge sleeps.
                let m = self.clone();
                let name = name.clone();
                tokio::spawn(async move { m.nudge_redraw(&name).await });
            }
        }

        // Nothing else will announce it: an adopted session's first frame usually classifies
        // as the state it was given.
        if adopted {
            let _ = self.events.send(Event::Sessions {
                sessions: self.views().await,
            });
        }
    }

    /// A SIGWINCH is the one event every TUI answers with a full redraw, which puts back the
    /// modes a text capture cannot carry - alternate screen, mouse reporting, cursor shape,
    /// bracketed paste.
    ///
    /// The size to come back to is read again *after* the shrink: a terminal window that
    /// reconnected mid-nudge has already stated its own shape, and restoring the figure this
    /// started with would strand the pane there - the mod only resends while the frames
    /// coming back disagree, and they no longer would.
    async fn nudge_redraw(self: &Arc<Self>, name: &str) {
        let Some((cols, rows)) = self.size_of(name).await else {
            return;
        };
        if cols < 2 {
            return;
        }
        if let Err(e) = self.tmux.resize(name, cols - 1, rows).await {
            tracing::debug!("redraw nudge {name}: {e:#}");
            return;
        }
        tokio::time::sleep(Duration::from_millis(60)).await;
        let Some((cols, rows)) = self.size_of(name).await else {
            return;
        };
        if let Err(e) = self.tmux.resize(name, cols, rows).await {
            tracing::debug!("redraw nudge {name}: {e:#}");
        }
    }

    async fn size_of(&self, name: &str) -> Option<(u16, u16)> {
        self.live.read().await.get(name).map(|l| (l.cols, l.rows))
    }

    /// A shortcut's agent is not in config by design, and a session started by hand under our
    /// socket is a colonist by this thing's own account. It carries no project - we cannot
    /// know what it was started with - so it lists with a blank directory and refuses to
    /// restart, while watching, typing and killing all work.
    async fn adopt(self: &Arc<Self>, name: &str) -> bool {
        let mut live = self.live.write().await;
        if live.contains_key(name) {
            return false;
        }
        tracing::info!("adopting tmux session {name} as a temporary agent");
        live.insert(
            name.to_string(),
            Live {
                cfg: SessionCfg {
                    name: name.to_string(),
                    ..Default::default()
                },
                ephemeral: true,
                state: State::Working,
                seq: 0,
                hash: 0,
                last_change: now_ms(),
                cols: BOOT_COLS,
                rows: BOOT_ROWS,
                screen: None,
                emu: None,
                reader: None,
            },
        );
        true
    }

    /// The order matters only for entries config has never heard of: a shortcut's agent, and
    /// anything adopted from under our socket.
    async fn session_cfg(&self, name: &str) -> Option<SessionCfg> {
        if let Some(s) = self.cfg.read().await.session(name) {
            return Some(s.clone());
        }
        self.live.read().await.get(name).map(|l| l.cfg.clone())
    }

    /// The same fallback `session_cfg` makes: an errand run in `temp` mode has a project
    /// nothing wrote down.
    async fn project_for(&self, cfg: &Config, s: &SessionCfg) -> Option<ProjectCfg> {
        if let Some(p) = cfg.project_of(s) {
            return Some(p.clone());
        }
        self.temp.read().await.get(&s.project).cloned()
    }

    pub async fn start(self: &Arc<Self>, name: &str) -> Result<()> {
        let cfg = self.config().await;
        let s = self
            .session_cfg(name)
            .await
            .ok_or_else(|| anyhow!("no such session: {name}"))?;

        let p = self.project_for(&cfg, &s).await.ok_or_else(|| {
            if s.project.is_empty() {
                anyhow!("session {name} belongs to no project")
            } else {
                anyhow!(
                    "session {name} belongs to project {}, which does not exist",
                    s.project
                )
            }
        })?;

        if self.tmux.exists(name).await {
            bail!("session {name} is already running");
        }
        // Before the directory is made: a name with no preset file behind it is a typo to
        // report, not a scratch directory to conjure up and an empty argv to hand tmux.
        if cfg.command_of(&s).trim().is_empty() {
            bail!("session {name} names command preset {:?}, which has no file", s.command);
        }
        let dir = expand(&p.dir);
        // A temporary project's directory is coined from its name, so the first agent to start
        // there brings it into being. Any other project names somewhere that already exists,
        // and a missing path is a typo worth reporting rather than a directory to conjure up.
        if p.temp {
            std::fs::create_dir_all(&dir).with_context(|| format!("making {dir}"))?;
        }
        if !std::path::Path::new(&dir).is_dir() {
            bail!("{dir} is not a directory");
        }

        // A restart with the terminal open comes back the shape the window asked for.
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
        // The one road to a dead session that does not go through the control reader: the
        // handle is aborted below, so the task never reaches `mark_down`.
        if self.is_ephemeral(name).await {
            self.forget(name).await;
            return Ok(());
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

    async fn is_ephemeral(&self, name: &str) -> bool {
        self.live
            .read()
            .await
            .get(name)
            .map(|l| l.ephemeral)
            .unwrap_or(false)
    }

    /// The reader is aborted last: the caller is sometimes that very task, and aborting the
    /// task you are running in cancels it at the next await - the one gathering the views
    /// nobody has been told about yet.
    async fn forget(self: &Arc<Self>, name: &str) {
        let (handle, project) = {
            let mut live = self.live.write().await;
            match live.remove(name) {
                Some(mut l) => (l.reader.take(), l.cfg.project.clone()),
                None => return,
            }
        };
        // The directory stays: what the errand did in there is worth reading afterwards, and
        // /tmp is the machine's to clear. A project the file owns is not in this table.
        self.temp.write().await.remove(&project);
        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });
        if let Some(h) = handle {
            h.abort();
        }
    }

    pub async fn restart(self: &Arc<Self>, name: &str) -> Result<()> {
        self.stop(name).await?;
        self.start(name).await
    }


    pub async fn projects(&self) -> Vec<ProjectCfg> {
        self.cfg.read().await.projects.clone()
    }

    /// Cheap on any change: a handful of them, moving when a person edits one.
    async fn announce_projects(&self) {
        let _ = self.events.send(Event::Projects {
            projects: self.projects().await,
        });
    }

    pub async fn add_project(self: &Arc<Self>, mut p: ProjectCfg) -> Result<()> {
        self.reload_if_changed().await;
        settle(&mut p);
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

    /// A changed name is a rename, and every session pointing at the old one is carried over
    /// in the same write; one left naming a project that no longer exists will not start.
    pub async fn update_project(self: &Arc<Self>, name: &str, mut p: ProjectCfg) -> Result<()> {
        self.reload_if_changed().await;
        settle(&mut p);
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
        // A running agent keeps the old directory and sandbox until it is restarted.
        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });
        Ok(())
    }

    /// Refused while anything still works there, the alternative being throwing agents away
    /// to tidy a list.
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


    pub async fn shortcuts(&self) -> Vec<ShortcutCfg> {
        self.cfg.read().await.shortcuts.clone()
    }

    async fn announce_shortcuts(&self) {
        let _ = self.events.send(Event::Shortcuts {
            shortcuts: self.shortcuts().await,
        });
    }

    pub async fn add_shortcut(self: &Arc<Self>, sc: ShortcutCfg) -> Result<()> {
        self.reload_if_changed().await;
        let mut cfg = self.cfg.write().await;
        check_shortcut(&cfg, &sc)?;
        if cfg.shortcut(&sc.name).is_some() {
            bail!("shortcut {} already exists", sc.name);
        }
        cfg.shortcuts.push(sc);
        self.save_cfg(&cfg)?;
        drop(cfg);
        self.announce_shortcuts().await;
        Ok(())
    }

    /// Nothing points at a shortcut by name, so a rename carries nothing with it.
    pub async fn update_shortcut(self: &Arc<Self>, name: &str, sc: ShortcutCfg) -> Result<()> {
        self.reload_if_changed().await;
        let mut cfg = self.cfg.write().await;
        check_shortcut(&cfg, &sc)?;
        let idx = cfg
            .shortcuts
            .iter()
            .position(|x| x.name == name)
            .ok_or_else(|| anyhow!("no such shortcut: {name}"))?;
        if sc.name != name && cfg.shortcut(&sc.name).is_some() {
            bail!("shortcut {} already exists", sc.name);
        }
        cfg.shortcuts[idx] = sc;
        self.save_cfg(&cfg)?;
        drop(cfg);
        self.announce_shortcuts().await;
        Ok(())
    }

    /// Nothing to refuse, unlike a project with agents in it: whatever this started is its
    /// own temporary agent now and outlives the entry.
    pub async fn remove_shortcut(self: &Arc<Self>, name: &str) -> Result<()> {
        self.reload_if_changed().await;
        let mut cfg = self.cfg.write().await;
        if cfg.shortcut(name).is_none() {
            bail!("no such shortcut: {name}");
        }
        cfg.shortcuts.retain(|s| s.name != name);
        self.save_cfg(&cfg)?;
        drop(cfg);
        self.announce_shortcuts().await;
        Ok(())
    }

    /// Returns as soon as the session is up, naming it; the typing happens in a task behind
    /// this, an agent being tens of seconds from ready where the client gives up after five.
    ///
    /// An override in `want` is honoured whatever the link says; an `ask` entry with nothing
    /// to go on is refused, guessing a project being guessing which repo an agent may write to.
    pub async fn run_shortcut(self: &Arc<Self>, name: &str, want: RunWhere) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self.config().await;
        let sc = cfg
            .shortcut(name)
            .ok_or_else(|| anyhow!("no such shortcut: {name}"))?
            .clone();
        check_shortcut(&cfg, &sc)?;

        let asked = match want.project.as_deref().map(str::trim) {
            Some(p) if !p.is_empty() => Some(p.to_string()),
            _ => None,
        };
        // An `ask` entry that named none is an errand nobody has said where to run yet.
        let fresh = want.temp || (asked.is_none() && sc.link == ShortcutLink::Temp);
        let named = match (&asked, sc.link) {
            _ if fresh => String::new(),
            (Some(p), _) => p.clone(),
            (None, ShortcutLink::Ask) => {
                bail!("shortcut {name} asks where to run; name a project or ask for a temporary one")
            }
            (None, _) => sc.project.clone(),
        };
        if !fresh && cfg.project(&named).is_none() {
            bail!("no such project: {named}");
        }
        // What a fresh project copies: the entry's own, when it names one.
        let template = cfg.project(&sc.project).cloned().unwrap_or_default();

        // Named and claimed without letting go of the table: two runs of the same errand at
        // once would otherwise pick the same free name, and the second would overwrite the
        // first's entry and then fail on tmux. `temp` taken after `live`, as everywhere.
        let session = {
            let mut live = self.live.write().await;
            let name = free_name(&live, &cfg, &slug(&sc.name));
            check_name(&name)?;

            let project = if fresh {
                let mut temp = self.temp.write().await;
                let pname = free_project_name(&cfg, &temp, &name);
                temp.insert(
                    pname.clone(),
                    ProjectCfg {
                        name: pname.clone(),
                        dir: crate::config::temp_dir(&pname),
                        temp: true,
                        ..template
                    },
                );
                pname
            } else {
                named
            };

            live.insert(
                name.clone(),
                Live {
                    // Before the start: that is where `start` reads the command and the pane
                    // size from, there being nothing in config for it to read.
                    cfg: cfg.session_for(&sc, name.clone(), project),
                    ephemeral: true,
                    state: State::Down,
                    seq: 0,
                    hash: 0,
                    last_change: 0,
                    cols: BOOT_COLS,
                    rows: BOOT_ROWS,
                    screen: None,
                    emu: None,
                    reader: None,
                },
            );
            name
        };

        if let Err(e) = self.start(&session).await {
            // Nothing came up, and the entry is the only trace of it either way.
            self.forget(&session).await;
            return Err(e);
        }

        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });

        let m = self.clone();
        let target = session.clone();
        let text = sc.text.clone();
        tokio::spawn(async move { m.deliver(&target, &text).await });

        Ok(session)
    }

    /// Two writes with a beat between: an agent's input box takes a pasted newline as a
    /// newline, so the submit has to arrive as a keypress after the paste has closed.
    async fn deliver(self: &Arc<Self>, name: &str, text: &str) {
        match self.wait_ready(name).await {
            Ready::Gone => {
                tracing::warn!("{name} was gone before its shortcut text could be sent");
                return;
            }
            Ready::Timeout => tracing::warn!(
                "{name} never went quiet after {}ms; sending its shortcut text anyway",
                READY_MS
            ),
            Ready::Settled => {}
        }

        if let Err(e) = self.paste(name, text).await {
            tracing::error!("sending shortcut text to {name}: {e:#}");
            return;
        }
        tokio::time::sleep(Duration::from_millis(ENTER_GAP_MS)).await;
        if let Err(e) = self.send_keys(name, vec!["Enter".into()], false).await {
            tracing::error!("submitting shortcut text in {name}: {e:#}");
        }
    }

    /// See `READY_MS` for why this is a settle rather than a match against anything an
    /// agent or a shell prints.
    async fn wait_ready(&self, name: &str) -> Ready {
        let deadline = now_ms() + READY_MS;
        let mut last_seq = u64::MAX;
        let mut still_since = 0u64;

        loop {
            let snap = {
                let live = self.live.read().await;
                live.get(name).map(|l| {
                    let printed = l
                        .screen
                        .as_ref()
                        .map(|s| s.lines.iter().any(|x| !strip_sgr(x).trim().is_empty()))
                        .unwrap_or(false);
                    (l.seq, printed, l.state)
                })
            };
            // Down and gone are the same answer: an ephemeral session that died has already
            // left the table.
            let Some((seq, printed, state)) = snap else {
                return Ready::Gone;
            };
            if state == State::Down {
                return Ready::Gone;
            }

            let now = now_ms();
            if !printed {
                still_since = 0;
            } else if seq != last_seq {
                last_seq = seq;
                still_since = now;
            } else if still_since > 0 && now.saturating_sub(still_since) >= SETTLE_MS {
                return Ready::Settled;
            }

            if now >= deadline {
                return Ready::Timeout;
            }
            tokio::time::sleep(Duration::from_millis(100)).await;
        }
    }


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

    /// A changed name is a rename: the agent keeps running, so tmux is renamed first - if it
    /// refuses, config.toml and reality stay in step.
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

    /// The control reader is pinned to the name it attached with, so it is dropped and
    /// re-attached under the new one - which re-seeds the emulator from a capture.
    ///
    /// The same rebuild a daemon restart does, so it wants the same nudge: a text capture
    /// carries no alternate screen, no mouse reporting and no cursor shape, and after a rename
    /// there is no other reason for the app to repaint - an agent sitting idle would wear the
    /// flattened screen until it next wrote something.
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
        if running && self.spawn_reader(new).await {
            // Off the critical path: the nudge sleeps.
            let m = self.clone();
            let name = new.to_string();
            tokio::spawn(async move { m.nudge_redraw(&name).await });
        }
    }

    pub async fn remove(self: &Arc<Self>, name: &str) -> Result<()> {
        self.reload_if_changed().await;
        // A temporary session is gone the moment it is stopped, and the file never had it.
        if self.is_ephemeral(name).await {
            return self.stop(name).await;
        }
        self.stop(name).await.ok();
        let mut cfg = self.cfg.write().await;
        cfg.sessions.retain(|s| s.name != name);
        self.save_cfg(&cfg)?;
        drop(cfg);
        self.sync_from_config().await;
        Ok(())
    }

    /// Sessions and state rules have their own endpoints and are never touched here.
    /// `bind`, `tmux_socket` and `poll_ms` are read once at startup, so those land in
    /// the file now and take hold when slopd restarts.
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
        // Both name command presets, and a name with no file behind it is a typo refused
        // where it was typed rather than an agent that will not start later.
        if let Some(d) = &defaults {
            let t = crate::presets::table();
            for (field, name) in [("agent", &d.agent), ("shell", &d.shell)] {
                let name = name.trim();
                if name.is_empty() {
                    bail!("the default {field} must name a command preset");
                }
                if t.command(name).is_none() {
                    bail!("unknown command preset: {name}");
                }
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
        self.announce_shortcuts().await;
        Ok(())
    }

    pub async fn views(&self) -> Vec<SessionView> {
        let cfg = self.config().await;
        let live = self.live.read().await;
        // Both tables: an errand running in a temporary project has one the file never heard
        // of, and a row that could not name its directory reads as pointing at a dead project.
        let temp = self.temp.read().await;
        let mut out: Vec<SessionView> = live
            .values()
            .map(|l| {
                let p = cfg.project_of(&l.cfg).or_else(|| temp.get(&l.cfg.project));
                SessionView {
                    name: l.cfg.name.clone(),
                    project: l.cfg.project.clone(),
                    dir: p.map(|p| p.dir.clone()).unwrap_or_default(),
                    command: l.cfg.command.clone(),
                    cmd: l.cfg.cmd.clone(),
                    sandbox: l.cfg.sandbox.clone(),
                    agent: cfg.command_of(&l.cfg),
                    state: l.state,
                    alive: l.state != State::Down,
                    cols: l.cols,
                    rows: l.rows,
                    net: p.map(|p| p.net).unwrap_or(true),
                    env: l.cfg.env.clone(),
                    autostart: l.cfg.autostart,
                    ephemeral: l.ephemeral,
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

    /// A no-op if the app isn't in a mouse mode: the mod only forwards when it saw
    /// `app_mouse`, but this is the authoritative emulator.
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

    /// Any end-marker inside the content is stripped, so a paste can't forge the terminator.
    /// Raw bytes, so escapes survive verbatim.
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
        // No rule hit: a pane that moved recently is still doing something.
        if changed || now_ms().saturating_sub(last_change) < IDLE_MS {
            State::Working
        } else {
            State::Idle
        }
    }

    /// No tmux spawns and no captures; screens arrive event-driven from the control readers.
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

    /// Idempotent. Reseeds the fresh emulator from the current pane, so an attach
    /// mid-session shows real content rather than a blank grid.
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
        // Scrollback and not just the visible pane: history is the half of a reattached
        // session that used to come back empty.
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

    /// Ends, marking the session down, when the session emits `%exit` or the control
    /// client's stdout closes.
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

        // The master is a blocking pty fd, bridged to async by a reader thread. Raw bytes and
        // not `String`: tmux writes UTF-8 literally in `%output` and can split a multibyte
        // char across chunks.
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
                        // Drop the LF and the CR pty ONLCR added ahead of it; tmux octal-escapes
                        // real control bytes, so a trailing CR is always that artifact.
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
        // One clipboard writer at a time: the tool is a process and the tick is 8ms, so an app
        // stating OSC 52 every frame would fork one per frame. While a write is in flight the
        // copy stays in the emulator, and what lands is the newest.
        let copying = Arc::new(AtomicBool::new(false));

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
                            // Cursor-position and device queries, or TUIs place their cursor wrongly.
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
                    None => break,
                },
                _ = flush.tick() => {
                    if dirty {
                        dirty = false;
                        self.render_and_broadcast(&name, &emu).await;
                    }
                    // An app that draws its own selection - Claude Code does - never sends the
                    // drag our way, so OSC 52 is the only word we get that anything was copied.
                    if !copying.load(Ordering::Relaxed) {
                        let clip = emu.lock().ok().and_then(|mut e| e.take_clip());
                        if let Some(text) = clip {
                            copying.store(true, Ordering::Relaxed);
                            let done = copying.clone();
                            let who = name.clone();
                            tokio::spawn(async move {
                                if let Err(e) = crate::clipboard::write(&text).await {
                                    tracing::debug!("osc52 clipboard {who}: {e:#}");
                                }
                                done.store(false, Ordering::Relaxed);
                            });
                        }
                    }
                }
            }
        }

        self.mark_down(&name).await;
    }

    async fn render_and_broadcast(&self, name: &str, emu: &Mutex<SessionEmu>) {
        let frame = match emu.lock() {
            Ok(e) => e.render(),
            Err(_) => return,
        };
        self.apply_frame(name, frame).await;
    }

    async fn apply_frame(&self, name: &str, frame: Frame) {
        let hash = hash_lines(&frame.lines);
        let (prev_hash, prev_state, prev_change, seq, prev_cursor, prev_meta, cols, rows) = {
            let live = self.live.read().await;
            let Some(l) = live.get(name) else { return };
            let cur = l.screen.as_ref().map(|s| (s.cx, s.cy)).unwrap_or((0, 0));
            let meta = l
                .screen
                .as_ref()
                .map(|s| {
                    (
                        s.cursor_shape,
                        s.cursor_blink,
                        s.app_mouse,
                        s.app_drag,
                        s.alt_screen,
                        s.title.clone(),
                    )
                })
                .unwrap_or_default();
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
            frame.app_drag,
            frame.alt_screen,
            frame.title.clone(),
        );
        let changed = hash != prev_hash || (frame.cx, frame.cy) != prev_cursor || meta != prev_meta;
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

    /// A temporary session has nowhere to be down, so it leaves the table entirely.
    async fn mark_down(self: &Arc<Self>, name: &str) {
        if self.is_ephemeral(name).await {
            self.forget(name).await;
            return;
        }

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

    /// Returned to the caller only, never broadcast, so it cannot clobber the live view.
    /// `off == 0` or beyond history returns the live frame.
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
        Some(ScreenView::from_frame(
            name, seq, cols, rows, achieved, frame,
        ))
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
    use std::collections::HashMap;

    use super::{
        check_name, free_name, free_project_name, settle, slug, strip_sgr, Live, State, BOOT_COLS,
        BOOT_ROWS,
    };
    use crate::config::{Config, ProjectCfg, SessionCfg};

    fn placeholder() -> Live {
        Live {
            cfg: SessionCfg::default(),
            ephemeral: true,
            state: State::Down,
            seq: 0,
            hash: 0,
            last_change: 0,
            cols: BOOT_COLS,
            rows: BOOT_ROWS,
            screen: None,
            emu: None,
            reader: None,
        }
    }

    /// A shortcut is named by a person and its session by tmux, so every slug has to come out
    /// the far end of `check_name`.
    #[test]
    fn slugs_are_names_tmux_accepts() {
        assert_eq!(slug("review diff"), "review-diff");
        assert_eq!(slug("run make test"), "run-make-test");
        assert_eq!(slug("v1.2 checks"), "v1-2-checks");
        assert_eq!(slug("  spaced  out  "), "spaced-out");
        assert_eq!(slug(" . "), "shortcut");
        assert_eq!(slug(""), "shortcut");

        for name in ["review diff", "v1.2 checks", "a/b", "", " . "] {
            assert!(check_name(&slug(name)).is_ok(), "slug of {name:?}");
        }
    }

    /// A standing agent in config and an errand still running are equally in the way.
    #[test]
    fn free_name_counts_up_past_config_and_the_live_table() {
        let mut cfg = Config::default();
        let mut live: HashMap<String, Live> = HashMap::new();

        assert_eq!(free_name(&live, &cfg, "review-diff"), "review-diff");

        cfg.sessions.push(SessionCfg {
            name: "review-diff".into(),
            ..Default::default()
        });
        assert_eq!(free_name(&live, &cfg, "review-diff"), "review-diff-2");

        live.insert("review-diff-2".into(), placeholder());
        assert_eq!(free_name(&live, &cfg, "review-diff"), "review-diff-3");
    }

    /// A project called "my stuff" would otherwise name a path with a space in it, and one
    /// called "../etc" somewhere else entirely.
    #[test]
    fn temp_projects_settle_on_a_path_under_the_root() {
        let mut p = ProjectCfg {
            name: "scratch pad".into(),
            temp: true,
            ..Default::default()
        };
        settle(&mut p);
        assert_eq!(p.dir, "/tmp/slopworld/scratch-pad");

        // Whatever was typed in the box is overwritten.
        p.dir = "/home/you/git/repo".into();
        settle(&mut p);
        assert_eq!(p.dir, "/tmp/slopworld/scratch-pad");

        // An ordinary project's directory is left as it was.
        let mut plain = ProjectCfg {
            name: "repo".into(),
            dir: "/home/you/git/repo".into(),
            ..Default::default()
        };
        settle(&mut plain);
        assert_eq!(plain.dir, "/home/you/git/repo");
    }

    /// Named after the agent running it, and dodging both tables.
    #[test]
    fn temp_project_names_dodge_both_tables() {
        let mut cfg = Config::default();
        let mut temp: HashMap<String, ProjectCfg> = HashMap::new();

        assert_eq!(free_project_name(&cfg, &temp, "review-diff"), "review-diff");

        cfg.projects.push(ProjectCfg {
            name: "review-diff".into(),
            ..Default::default()
        });
        assert_eq!(
            free_project_name(&cfg, &temp, "review-diff"),
            "review-diff-2"
        );

        temp.insert("review-diff-2".into(), ProjectCfg::default());
        assert_eq!(
            free_project_name(&cfg, &temp, "review-diff"),
            "review-diff-3"
        );
    }

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
