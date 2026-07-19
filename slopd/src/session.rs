use std::collections::HashMap;
use std::hash::{Hash, Hasher};
use std::path::PathBuf;
use std::sync::{Arc, Mutex};
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use anyhow::{anyhow, bail, Result};
use regex::Regex;
use serde::{Deserialize, Serialize};
use tokio::io::{AsyncBufReadExt, BufReader};
use tokio::sync::{broadcast, RwLock};
use tokio::task::JoinHandle;
use tokio::time::{interval, MissedTickBehavior};

use crate::config::{expand, Config, SessionCfg};
use crate::emu::{parse_output, Frame, SessionEmu};
use crate::sandbox::build_argv;
use crate::tmux::Tmux;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum State {
    /// tmux session is gone: either never started or the agent exited.
    Dead,
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
    pub dir: String,
    pub agent: String,
    pub state: State,
    pub alive: bool,
    pub cols: u16,
    pub rows: u16,
    pub net: bool,
    pub sandbox: bool,
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
    /// One entry per row, still carrying SGR escapes.
    pub lines: Vec<String>,
}

#[derive(Debug, Clone, Serialize)]
#[serde(tag = "t", rename_all = "lowercase")]
pub enum Event {
    Sessions { sessions: Vec<SessionView> },
    Screen { screen: ScreenView },
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
    pub events: broadcast::Sender<Event>,
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

impl Manager {
    pub async fn new(cfg: Config, cfg_path: PathBuf) -> Arc<Self> {
        let (events, _) = broadcast::channel(256);
        let m = Arc::new(Self {
            tmux: Tmux::new(cfg.daemon.tmux_socket.clone()),
            cfg_path,
            rules: RwLock::new(compile_rules(&cfg)),
            live: RwLock::new(HashMap::new()),
            cfg: RwLock::new(cfg),
            events,
        });
        m.sync_from_config().await;
        m
    }

    pub async fn config(&self) -> Config {
        self.cfg.read().await.clone()
    }

    /// Reconciles the live table with config: adds new entries, drops removed
    /// ones, autostarts what asks for it.
    pub async fn sync_from_config(self: &Arc<Self>) {
        let cfg = self.config().await;
        let mut live = self.live.write().await;

        live.retain(|name, _| cfg.session(name).is_some());

        for s in &cfg.sessions {
            let cols = s.cols.unwrap_or(cfg.defaults.cols);
            let rows = s.rows.unwrap_or(cfg.defaults.rows);
            live.entry(s.name.clone())
                .and_modify(|l| l.cfg = s.clone())
                .or_insert(Live {
                    cfg: s.clone(),
                    state: State::Dead,
                    seq: 0,
                    hash: 0,
                    last_change: 0,
                    cols,
                    rows,
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
        for name in self.tmux.list().await {
            if self.live.read().await.contains_key(&name) {
                self.spawn_reader(&name).await;
            }
        }
    }

    pub async fn start(self: &Arc<Self>, name: &str) -> Result<()> {
        let cfg = self.config().await;
        let s = cfg
            .session(name)
            .ok_or_else(|| anyhow!("no such session: {name}"))?
            .clone();

        if self.tmux.exists(name).await {
            bail!("session {name} is already running");
        }
        let dir = expand(&s.dir);
        if !std::path::Path::new(&dir).is_dir() {
            bail!("{dir} is not a directory");
        }

        let cols = s.cols.unwrap_or(cfg.defaults.cols);
        let rows = s.rows.unwrap_or(cfg.defaults.rows);
        let argv = build_argv(&cfg, &s);
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
            l.state = State::Dead;
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

    /// Adds a session to config, persists, and starts it if asked.
    pub async fn add(self: &Arc<Self>, s: SessionCfg) -> Result<()> {
        let mut cfg = self.cfg.write().await;
        if cfg.session(&s.name).is_some() {
            bail!("session {} already exists", s.name);
        }
        if s.name.is_empty() || s.name.contains(|c: char| c.is_whitespace() || c == ':' || c == '.')
        {
            bail!("session name must be non-empty and free of whitespace, ':' and '.'");
        }
        let autostart = s.autostart;
        let name = s.name.clone();
        cfg.sessions.push(s);
        cfg.save(&self.cfg_path)?;
        drop(cfg);

        self.sync_from_config().await;
        if autostart {
            self.start(&name).await.ok();
        }
        Ok(())
    }

    pub async fn update(self: &Arc<Self>, name: &str, s: SessionCfg) -> Result<()> {
        let mut cfg = self.cfg.write().await;
        let idx = cfg
            .sessions
            .iter()
            .position(|x| x.name == name)
            .ok_or_else(|| anyhow!("no such session: {name}"))?;
        cfg.sessions[idx] = s;
        cfg.save(&self.cfg_path)?;
        drop(cfg);
        self.sync_from_config().await;
        Ok(())
    }

    pub async fn remove(self: &Arc<Self>, name: &str) -> Result<()> {
        self.stop(name).await.ok();
        let mut cfg = self.cfg.write().await;
        cfg.sessions.retain(|s| s.name != name);
        cfg.save(&self.cfg_path)?;
        drop(cfg);
        self.sync_from_config().await;
        Ok(())
    }

    pub async fn replace_config(self: &Arc<Self>, text: &str) -> Result<()> {
        let new = Config::parse(text)?;
        new.save(&self.cfg_path)?;
        *self.rules.write().await = compile_rules(&new);
        *self.cfg.write().await = new;
        self.sync_from_config().await;
        Ok(())
    }

    pub async fn views(&self) -> Vec<SessionView> {
        let cfg = self.config().await;
        let live = self.live.read().await;
        let mut out: Vec<SessionView> = live
            .values()
            .map(|l| SessionView {
                name: l.cfg.name.clone(),
                dir: l.cfg.dir.clone(),
                agent: l
                    .cfg
                    .agent
                    .clone()
                    .unwrap_or_else(|| cfg.defaults.agent.clone()),
                state: l.state,
                alive: l.state != State::Dead,
                cols: l.cols,
                rows: l.rows,
                net: l.cfg.net,
                sandbox: l.cfg.sandbox,
                last_change: l.last_change,
                seq: l.seq,
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
        // No rule hit: a pane that moved in the last second is still doing something.
        if changed || now_ms().saturating_sub(last_change) < 1000 {
            State::Working
        } else {
            State::Idle
        }
    }

    /// A cheap in-memory tick over every live session: re-runs the state rules so
    /// a session that fell quiet decays working -> idle. No tmux spawns and no
    /// captures; screen changes arrive event-driven from the control readers.
    pub async fn retick(&self) {
        // Snapshot outside the lock so classify()'s await doesn't hold it.
        let snapshot: Vec<(String, u64, String)> = {
            let live = self.live.read().await;
            live.iter()
                .filter(|(_, l)| l.state != State::Dead)
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
    async fn spawn_reader(self: &Arc<Self>, name: &str) {
        let (cols, rows, has_emu) = {
            let live = self.live.read().await;
            match live.get(name) {
                Some(l) => (l.cols, l.rows, l.emu.is_some()),
                None => return,
            }
        };
        if has_emu {
            return;
        }

        let mut e = SessionEmu::new(cols, rows);
        if let Ok(cap) = self.tmux.capture(name).await {
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
                    return;
                }
            }
        }

        // Push the seeded frame so fresh subscribers see content before any output.
        self.render_and_broadcast(name, &emu).await;
    }

    /// The control-reader task: pumps `%output` bytes into the emulator and, on a
    /// coalescing tick, renders + broadcasts. Ends (marking the session dead) when
    /// the session emits `%exit` or the control client's stdout closes.
    async fn run_control(self: Arc<Self>, name: String, emu: Arc<Mutex<SessionEmu>>) {
        let mut child = match self.tmux.control_attach(&name) {
            Ok(c) => c,
            Err(e) => {
                tracing::error!("control attach {name}: {e:#}");
                self.mark_dead(&name).await;
                return;
            }
        };
        let Some(stdout) = child.stdout.take() else {
            self.mark_dead(&name).await;
            return;
        };
        let mut lines = BufReader::new(stdout).lines();

        // Coalesce bursts of output into ~60fps renders instead of one per line.
        let mut flush = interval(Duration::from_millis(8));
        flush.set_missed_tick_behavior(MissedTickBehavior::Delay);
        let mut dirty = false;

        loop {
            tokio::select! {
                line = lines.next_line() => match line {
                    Ok(Some(l)) => {
                        if let Some(bytes) = parse_output(&l) {
                            if let Ok(mut e) = emu.lock() {
                                e.feed(&bytes);
                            }
                            dirty = true;
                        } else if l.starts_with("%exit") {
                            break;
                        }
                    }
                    // stdout closed or a read error: the client is gone.
                    _ => break,
                },
                _ = flush.tick() => {
                    if dirty {
                        dirty = false;
                        self.render_and_broadcast(&name, &emu).await;
                    }
                }
            }
        }

        self.mark_dead(&name).await;
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
        let (prev_hash, prev_state, prev_change, seq, prev_cursor, cols, rows) = {
            let live = self.live.read().await;
            let Some(l) = live.get(name) else { return };
            let cur = l.screen.as_ref().map(|s| (s.cx, s.cy)).unwrap_or((0, 0));
            (
                l.hash,
                l.state,
                l.last_change,
                l.seq,
                cur,
                l.cols,
                l.rows,
            )
        };

        let changed = hash != prev_hash || (frame.cx, frame.cy) != prev_cursor;
        let plain = strip_sgr(&frame.lines.join("\n"));
        let state = self.classify(changed, prev_change, &plain).await;

        if !changed && state == prev_state {
            return;
        }

        let view = ScreenView {
            name: name.to_string(),
            seq: seq + 1,
            cols,
            rows,
            cx: frame.cx,
            cy: frame.cy,
            off: 0,
            lines: frame.lines,
        };

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

    /// Marks a session dead and tears down its emulator + reader. Broadcasts a
    /// session list only when the state actually moved.
    async fn mark_dead(&self, name: &str) {
        let mut changed = false;
        {
            let mut live = self.live.write().await;
            if let Some(l) = live.get_mut(name) {
                if l.state != State::Dead {
                    l.state = State::Dead;
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
        Some(ScreenView {
            name: name.to_string(),
            seq,
            cols,
            rows,
            cx: frame.cx,
            cy: frame.cy,
            off: achieved,
            lines: frame.lines,
        })
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
    use super::strip_sgr;

    #[test]
    fn strips_colour_and_keeps_text() {
        assert_eq!(strip_sgr("\x1b[31mred\x1b[0m done"), "red done");
        assert_eq!(strip_sgr("esc to interrupt"), "esc to interrupt");
        assert_eq!(strip_sgr("\x1b]0;title\x07body"), "body");
        assert_eq!(strip_sgr("\x1b[38;5;214m❯ 1.\x1b[m"), "❯ 1.");
    }
}
