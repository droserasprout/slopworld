use std::collections::HashMap;
use std::hash::{Hash, Hasher};
use std::path::{Component, Path, PathBuf};
use std::process::Stdio;
use std::sync::atomic::{AtomicBool, AtomicU64, AtomicUsize, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use anyhow::{anyhow, bail, Context, Result};
use regex::Regex;
use serde::{Deserialize, Serialize};
use serde_json::Value;
use tokio::io::{AsyncRead, AsyncReadExt};
use tokio::sync::{broadcast, mpsc, RwLock};
use tokio::task::JoinHandle;

use crate::config::{
    expand, Config, Limits, NetworkMode, ProjectCfg, SessionCfg, ShortcutCfg, ShortcutKind,
    ShortcutLink, TitlePolicy,
};
use crate::emu::{parse_output, Frame, MouseInput, SessionEmu};
use crate::sandbox::build_argv;
use crate::tmux::Tmux;
use tokio::process::Command;

const IDLE_MS: u64 = 10_000;

// Limit stale prompts near the top of a screen from overriding newer status below them.
const TAIL_LINES: usize = 12;

// Unwatched panes still need classification, but not reader-rate rendering.
const UNWATCHED_MS: u64 = 200;

const FILE_ACTION_TIMEOUT: Duration = Duration::from_secs(15);
const FILE_ACTION_STREAM_LIMIT: usize = 4096;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum State {
    Down,
    Working,
    Waiting,
    Idle,
}

#[derive(Debug, Clone, Serialize)]
pub struct SessionView {
    pub name: String,
    pub project: String,
    pub dir: String,
    pub command: String,
    /// The command preset after `[defaults] agent` is resolved. Empty means the session
    /// supplies its own command line, so it has no command preset's sandbox.
    pub command_preset: String,
    pub cmd: Option<String>,
    pub sandbox: Vec<String>,
    pub breadcrumbs: Vec<String>,
    pub breadcrumb_yolo: bool,
    // Lets the client attach tips only to the Enter that will consume breadcrumbs.
    pub breadcrumbs_pending: bool,
    pub agent: String,
    pub state: State,
    pub alive: bool,
    pub cols: u16,
    pub rows: u16,
    /// The effective project ceiling or agent reduction.
    pub network: NetworkMode,
    /// Null means the agent inherits the project setting.
    pub network_override: Option<NetworkMode>,
    /// The caps this agent runs under, its own merged over its project's.
    pub limits: Limits,
    /// This agent's own caps before project inheritance - what the editor edits.
    pub limits_override: Limits,
    pub autostart: bool,
    // Ephemeral sessions have no editable config entry and die with their process.
    pub ephemeral: bool,
    pub last_change: u64,
    // Separate from output activity so a continuously-redrawing worker can still age.
    pub state_since: u64,
    pub title: String,
    // Sticky until someone subscribes, rather than tied to the frame that rang.
    pub bell: bool,
    pub seq: u64,
}

#[derive(Debug, Clone, Default, Deserialize)]
pub struct RunWhere {
    #[serde(default)]
    pub project: Option<String>,
    #[serde(default)]
    pub temp: bool,
    #[serde(default)]
    pub random_tips: Vec<String>,
}

#[derive(Debug, Clone, Serialize)]
pub struct ScreenView {
    pub name: String,
    pub seq: u64,
    pub cols: u16,
    pub rows: u16,
    pub cx: u16,
    pub cy: u16,
    #[serde(default)]
    pub off: u32,
    #[serde(default)]
    pub cursor_shape: u8,
    #[serde(default)]
    pub cursor_blink: bool,
    #[serde(default)]
    pub app_mouse: bool,
    // Distinguishes application mouse drags from terminal text selection.
    #[serde(default)]
    pub app_drag: bool,
    // Alternate screens have no scrollback; clients route wheel input differently.
    #[serde(default)]
    pub alt_screen: bool,
    #[serde(default)]
    pub title: String,
    // Echoes a one-off scroll request; live broadcasts use zero.
    #[serde(default)]
    pub request_id: u64,
    pub lines: Vec<String>,
}

impl ScreenView {
    fn from_frame(
        name: &str,
        seq: u64,
        cols: u16,
        rows: u16,
        off: u32,
        frame: Frame,
        request_id: u64,
    ) -> Self {
        Self {
            name: name.to_string(),
            seq,
            cols,
            rows,
            cx: frame.cx,
            cy: frame.cy,
            off,
            request_id,
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
    Sessions { sessions: Vec<SessionView> },
    Projects { projects: Vec<ProjectCfg> },
    Shortcuts { shortcuts: Vec<ShortcutCfg> },
    Screen { screen: ScreenView },
    Usage { usage: crate::usage::Snapshot },
    Audio { audio: crate::audio::AudioState },
    Jukebox { jukebox: crate::jukebox::Catalog },
    Quit,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Ready {
    Settled,
    // Startup continues in the background; only Gone cancels queued typing.
    Timeout,
    Gone,
}

struct Live {
    cfg: SessionCfg,
    ephemeral: bool,
    // Never persisted: config entries are not allowed to request host execution.
    host: bool,
    state: State,
    seq: u64,
    // Last sequence classified by retick; equal means only idle decay can change state.
    retick_seq: u64,
    hash: u64,
    last_change: u64,
    // State time is independent of pane redraws, which can continue several times a second.
    state_since: u64,
    // Sticky until a client subscribes; the frame containing the bell is transient.
    bell: bool,
    cols: u16,
    rows: u16,
    plain: String,
    screen: Option<ScreenView>,
    emu: Option<Arc<Mutex<SessionEmu>>>,
    reader: Option<JoinHandle<()>>,
    input: Option<mpsc::UnboundedSender<Input>>,
    // Spliced immediately before the first Enter after process start.
    breadcrumbs: Vec<u8>,
    breadcrumbs_pending: bool,
    title: TitleCapture,
}

impl Live {
    fn set_state(&mut self, s: State) -> bool {
        if self.state == s {
            return false;
        }
        self.state = s;
        self.state_since = now_ms();
        true
    }
}

struct TitleCapture {
    composer: Composer,
    conversation: u64,
    generation: u64,
    pending: bool,
    override_title: Option<String>,
}

#[derive(Clone, Copy)]
enum TitleAgent {
    Codex,
    Pi,
}

/// The preset name is the usual case, but sessions may supply an explicit command line.
/// Those should retain the same title behavior as their corresponding preset.
fn title_agent(cfg: &Config, session: &SessionCfg) -> Option<TitleAgent> {
    let command = cfg.command_name(session);
    let executable = if command.is_empty() {
        crate::sandbox::shell_split(&cfg.command_of(session))
            .into_iter()
            .next()?
    } else {
        command
    };
    match Path::new(&executable).file_name()?.to_str()? {
        "codex" => Some(TitleAgent::Codex),
        "pi" => Some(TitleAgent::Pi),
        _ => None,
    }
}

fn title_settings(cfg: &Config, agent: TitleAgent) -> (TitlePolicy, String) {
    match agent {
        TitleAgent::Codex => (cfg.daemon.agent_titles, cfg.daemon.title_model.clone()),
        TitleAgent::Pi => (cfg.daemon.pi_titles, cfg.daemon.pi_title_model.clone()),
    }
}

impl Default for TitleCapture {
    fn default() -> Self {
        Self {
            composer: Composer::ready(),
            conversation: 0,
            generation: 0,
            pending: false,
            override_title: None,
        }
    }
}

#[derive(Default)]
struct Composer {
    text: Vec<char>,
    cursor: usize,
    certain: bool,
}

enum Submission {
    Prompt(String),
    New(Option<String>),
}

// Waiting screens normally consume a one-word approval or picker choice. Keep those out of
// OpenRouter, but do not discard a real prompt just because the last captured frame still says
// waiting while the agent's input line is active.
fn is_dialog_answer(prompt: &str) -> bool {
    matches!(
        prompt.trim().to_ascii_lowercase().as_str(),
        "y" | "yes" | "n" | "no" | "1" | "2" | "a" | "b" | "ok" | "okay" | "cancel"
    )
}

impl Composer {
    fn ready() -> Self {
        Self {
            certain: true,
            ..Self::default()
        }
    }

    fn literal(&mut self, text: &str) {
        // The mod encodes Shift+Enter with kitty's keyboard protocol. Codex receives a
        // multiline edit; keeping the escape bytes would poison the mirrored prompt.
        if text == "\x1b[13;2u" {
            self.text.insert(self.cursor, '\n');
            self.cursor += 1;
            return;
        }
        for ch in text.chars() {
            self.text.insert(self.cursor, ch);
            self.cursor += 1;
        }
    }

    fn paste(&mut self, text: &str) {
        self.literal(text);
    }

    fn key(&mut self, key: &str) -> Option<Submission> {
        match key {
            "Enter" => return self.submit(),
            "BSpace" if self.cursor > 0 => {
                self.cursor -= 1;
                self.text.remove(self.cursor);
            }
            "DC" if self.cursor < self.text.len() => {
                self.text.remove(self.cursor);
            }
            "Left" if self.cursor > 0 => self.cursor -= 1,
            "Right" if self.cursor < self.text.len() => self.cursor += 1,
            "Home" | "C-a" => self.cursor = 0,
            "End" | "C-e" => self.cursor = self.text.len(),
            "C-u" => {
                self.text.drain(..self.cursor);
                self.cursor = 0;
            }
            "C-k" => self.text.truncate(self.cursor),
            "C-w" => {
                while self.cursor > 0 && self.text[self.cursor - 1].is_whitespace() {
                    self.cursor -= 1;
                    self.text.remove(self.cursor);
                }
                while self.cursor > 0 && !self.text[self.cursor - 1].is_whitespace() {
                    self.cursor -= 1;
                    self.text.remove(self.cursor);
                }
            }
            // An interrupt cancels the input rather than making the next Enter submit stale text.
            "C-c" => *self = Self::ready(),
            // History, completion, word-wise movement and TUI controls mean our mirror no
            // longer proves what Codex will receive. Keep forwarding, but skip this submission.
            _ => self.certain = false,
        }
        None
    }

    fn submit(&mut self) -> Option<Submission> {
        let certain = self.certain;
        let text: String = self.text.iter().collect();
        *self = Self::ready();
        if !certain {
            return None;
        }
        let text = text.trim();
        if text.is_empty() {
            return None;
        }
        if text == "/new" {
            return Some(Submission::New(None));
        }
        if let Some(name) = text
            .strip_prefix("/new ")
            .map(str::trim)
            .filter(|s| !s.is_empty())
        {
            return Some(Submission::New(Some(name.to_string())));
        }
        if text.starts_with('/') {
            return None;
        }
        Some(Submission::Prompt(text.to_string()))
    }
}

struct TitleRequest {
    prompt: String,
    conversation: u64,
    generation: u64,
    key_file: String,
    model: String,
}

enum Input {
    Keys { keys: Vec<String>, literal: bool },
    Bytes(Vec<u8>),
    // Kept whole so the receiving application observes one paste.
    Paste(Vec<u8>),
    // Executed by the single consumer, preserving the pause relative to queued input.
    Gap(Duration),
}

// tmux -H spends one argv entry per byte and its command message is limited to about 1 KiB.
const INPUT_BATCH: usize = 800;
const INPUT_KEYS: usize = 100;

fn merge_input(items: Vec<Input>) -> Vec<Input> {
    let mut out: Vec<Input> = Vec::new();
    for item in items {
        let merged = match (out.last_mut(), &item) {
            (Some(Input::Bytes(acc)), Input::Bytes(b)) if acc.len() + b.len() <= INPUT_BATCH => {
                acc.extend_from_slice(b);
                true
            }
            (
                Some(Input::Keys {
                    keys: acc,
                    literal: had,
                }),
                Input::Keys { keys, literal },
            ) if *had == *literal && acc.len() + keys.len() <= INPUT_KEYS => {
                acc.extend(keys.iter().cloned());
                true
            }
            _ => false,
        };
        if !merged {
            out.push(item);
        }
    }
    out
}

pub struct Manager {
    pub tmux: Tmux,
    pub cfg_path: PathBuf,
    cfg: RwLock<Config>,
    live: RwLock<HashMap<String, Live>>,
    temp: RwLock<HashMap<String, ProjectCfg>>,
    rules: RwLock<Vec<(State, Regex)>>,
    cfg_mtime: Mutex<Option<SystemTime>>,
    presets_mtime: Mutex<Option<SystemTime>>,
    jukebox_mtime: Mutex<Option<SystemTime>>,
    cfg_checked: AtomicU64,
    usage: RwLock<crate::usage::Snapshot>,
    clients: AtomicUsize,
    clients_since: AtomicU64,
    watchers: Mutex<HashMap<String, usize>>,
    scroll_cache: Mutex<HashMap<String, CachedScroll>>,
    pub audio: crate::audio::Audio,
    pub events: broadcast::Sender<Event>,
    grants: RwLock<crate::grant::Grants>,
    tasks: Mutex<crate::tasks::Tasks>,
}

struct CachedScroll {
    live_seq: u64,
    off: u32,
    cols: u16,
    rows: u16,
    view: ScreenView,
}

pub struct ClientGuard(Arc<Manager>);

impl Drop for ClientGuard {
    fn drop(&mut self) {
        self.0.clients.fetch_sub(1, Ordering::Relaxed);
    }
}

pub struct WatchGuard(Arc<Manager>, String);

impl Drop for WatchGuard {
    fn drop(&mut self) {
        let Ok(mut w) = self.0.watchers.lock() else {
            return;
        };
        if let Some(n) = w.get_mut(&self.1) {
            *n -= 1;
            if *n == 0 {
                w.remove(&self.1);
            }
        }
    }
}

const CFG_CHECK_MS: u64 = 2_000;

const QUIT_WAIT_TICKS: usize = 120;

const BOOT_COLS: u16 = 120;
const BOOT_ROWS: u16 = 34;

const READY_MS: u64 = 30_000;
const SETTLE_MS: u64 = 750;
const ENTER_GAP_MS: u64 = 150;

fn disk_mtime(path: &std::path::Path) -> Option<SystemTime> {
    std::fs::metadata(path).ok()?.modified().ok()
}

struct TemplateVars<'a> {
    agent: &'a str,
    project: &'a str,
    directory: &'a str,
    command: &'a str,
}

fn render_template_with(text: &str, random_tips: &[String], vars: Option<&TemplateVars>) -> String {
    let mut next = 0usize;
    let mut out = String::with_capacity(text.len());
    let mut rest = text;
    while let Some(start) = rest.find("{{") {
        let Some(end_rel) = rest[start + 2..].find("}}") else {
            break;
        };
        let end = start + 2 + end_rel;
        out.push_str(&rest[..start]);
        let key = rest[start + 2..end].trim();
        let value = match (key, vars) {
            ("random_tip", _) if !random_tips.is_empty() => {
                let value = &random_tips[next % random_tips.len()];
                next += 1;
                Some(value.as_str())
            }
            ("agent", Some(v)) => Some(v.agent),
            ("project", Some(v)) => Some(v.project),
            ("directory", Some(v)) => Some(v.directory),
            ("command", Some(v)) => Some(v.command),
            _ => None,
        };
        if let Some(value) = value {
            out.push_str(value);
        } else {
            out.push_str(&rest[start..end + 2]);
        }
        rest = &rest[end + 2..];
    }
    out.push_str(rest);
    out
}

fn render_template(text: &str, random_tips: &[String]) -> String {
    render_template_with(text, random_tips, None)
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

fn match_rules(rules: &[(State, Regex)], text: &str) -> Option<State> {
    let lines: Vec<&str> = text.lines().collect();
    let end = lines
        .iter()
        .rposition(|l| !l.trim().is_empty())
        .map_or(0, |i| i + 1);
    // The lowest matching line wins; config order only breaks ties on that line.
    for line in lines[end.saturating_sub(TAIL_LINES)..end].iter().rev() {
        for (state, re) in rules {
            if re.is_match(line) {
                return Some(*state);
            }
        }
    }
    None
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

fn check_name(name: &str) -> Result<()> {
    if name.is_empty() || name.contains(|c: char| c.is_whitespace() || c == ':' || c == '.') {
        bail!("session name must be non-empty and free of whitespace, ':' and '.'");
    }
    Ok(())
}

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

fn check_project(p: &ProjectCfg) -> Result<()> {
    if p.name.trim().is_empty() {
        bail!("project name must not be empty");
    }
    if p.dir.trim().is_empty() {
        bail!("project {} needs a directory", p.name);
    }
    let dir = expand(&p.dir);
    if let Some(what) = crate::sandbox::refused(&dir) {
        bail!("project {} cannot live at {dir}: it reaches {what}", p.name);
    }
    check_presets(&p.sandbox)?;
    Ok(())
}

fn check_presets(names: &[String]) -> Result<()> {
    let t = crate::presets::table();
    for name in names {
        if t.sandbox(name).is_none() {
            bail!("unknown sandbox preset: {name}");
        }
    }
    Ok(())
}

fn breadcrumb_block(crumbs: &[String]) -> String {
    let mut out = String::new();
    let mut in_list = false;
    for crumb in crumbs {
        let crumb = crumb.trim();
        if crumb.is_empty() {
            continue;
        }
        if crumb.contains('\n') {
            out.push_str("\n\n");
            out.push_str(crumb);
            in_list = false;
        } else {
            out.push_str(if in_list { "\n" } else { "\n\n" });
            out.push_str("- ");
            out.push_str(crumb);
            in_list = true;
        }
    }
    out
}

fn check_breadcrumbs(cfg: &Config, names: &[String]) -> Result<()> {
    for name in names {
        match cfg.shortcut(name) {
            Some(sc) if sc.kind == ShortcutKind::Breadcrumb => {}
            Some(_) => bail!("shortcut {name} is not a breadcrumb"),
            None => bail!("unknown breadcrumb: {name}"),
        }
    }
    Ok(())
}

fn check_shortcut(cfg: &Config, sc: &ShortcutCfg) -> Result<()> {
    if sc.name.trim().is_empty() {
        bail!("shortcut name must not be empty");
    }
    if sc.kind == ShortcutKind::Breadcrumb {
        if sc.text.trim().is_empty() {
            bail!("breadcrumb {} has no text", sc.name);
        }
        if !sc.project.trim().is_empty() && cfg.project(&sc.project).is_none() {
            bail!("no such project: {}", sc.project);
        }
        return Ok(());
    }
    if sc.kind == ShortcutKind::FileAction {
        if sc
            .command
            .as_deref()
            .map(str::trim)
            .unwrap_or("")
            .is_empty()
        {
            bail!("file action {} has no command", sc.name);
        }
        if !sc.text.trim().is_empty() {
            bail!("file action {} has unexpected text", sc.name);
        }
        return Ok(());
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

fn settle(p: &mut ProjectCfg) {
    if p.temp {
        p.dir = crate::config::temp_dir(&slug(&p.name));
    }
}

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

fn check_belongs(cfg: &Config, s: &SessionCfg) -> Result<()> {
    if s.project.trim().is_empty() {
        bail!("session {} must belong to a project", s.name);
    }
    let Some(project) = cfg.project(&s.project) else {
        bail!("no such project: {}", s.project);
    };
    cfg.network_of(s, project)?;
    check_presets(&s.sandbox)?;
    check_breadcrumbs(cfg, &s.breadcrumbs)?;
    Ok(())
}

fn normalize_path(path: &Path) -> PathBuf {
    let mut out = PathBuf::new();
    for component in path.components() {
        match component {
            Component::RootDir => out.push(Path::new("/")),
            Component::CurDir => {}
            Component::ParentDir => {
                out.pop();
            }
            Component::Normal(name) => out.push(name),
            Component::Prefix(prefix) => out.push(prefix.as_os_str()),
        }
    }
    out
}

fn absolute_path(raw: &str) -> Result<PathBuf> {
    if raw.trim().is_empty() {
        bail!("no file action path");
    }
    let expanded = expand(raw);
    let path = Path::new(&expanded);
    let path = if path.is_absolute() {
        path.to_path_buf()
    } else {
        std::env::current_dir()?.join(path)
    };
    Ok(normalize_path(&path))
}

fn project_action_path(project: &ProjectCfg, raw: &str) -> Result<PathBuf> {
    let root = absolute_path(&project.dir)?;
    let path = absolute_path(raw)?;
    if !path.starts_with(&root) {
        bail!("file action path is outside project {}", project.name);
    }
    Ok(path)
}

fn shell_quote(path: &str) -> String {
    format!("'{}'", path.replace('\'', "'\\''"))
}

fn normalize_action_command(raw_path: &str, path: &Path, command: &str) -> String {
    let raw = shell_quote(raw_path);
    let absolute = shell_quote(&path.to_string_lossy());
    command.replace(&raw, &absolute)
}

async fn read_action_output<R: AsyncRead + Unpin>(mut stream: R) -> Result<(Vec<u8>, bool)> {
    let mut output = Vec::new();
    let mut truncated = false;
    let mut buffer = [0u8; 4096];
    loop {
        let read = stream.read(&mut buffer).await?;
        if read == 0 {
            break;
        }
        let room = FILE_ACTION_STREAM_LIMIT.saturating_sub(output.len());
        let take = room.min(read);
        output.extend_from_slice(&buffer[..take]);
        if take < read {
            truncated = true;
        }
    }
    Ok((output, truncated))
}

impl Manager {
    pub async fn new(cfg: Config, cfg_path: PathBuf) -> Arc<Self> {
        let (events, _) = broadcast::channel(256);
        let mtime = disk_mtime(&cfg_path);
        let tasks = crate::tasks::Tasks::load(&cfg_path)
            .unwrap_or_else(|e| panic!("task store {}: {e:#}", cfg_path.display()));
        let m = Arc::new(Self {
            tmux: Tmux::new(cfg.daemon.tmux_socket.clone(), cfg.daemon.history_limit),
            cfg_path,
            rules: RwLock::new(compile_rules(&cfg)),
            live: RwLock::new(HashMap::new()),
            temp: RwLock::new(HashMap::new()),
            cfg: RwLock::new(cfg),
            cfg_mtime: Mutex::new(mtime),
            presets_mtime: Mutex::new(crate::presets::dir_stamp()),
            jukebox_mtime: Mutex::new(crate::jukebox::dir_stamp()),
            cfg_checked: AtomicU64::new(0),
            usage: RwLock::new(crate::usage::Snapshot::default()),
            clients: AtomicUsize::new(0),
            clients_since: AtomicU64::new(0),
            watchers: Mutex::new(HashMap::new()),
            scroll_cache: Mutex::new(HashMap::new()),
            audio: crate::audio::Audio::new(),
            events,
            grants: RwLock::new(crate::grant::Grants::default()),
            tasks: Mutex::new(tasks),
        });
        if let Ok(n) = crate::sandbox::purge_trash() {
            if n > 0 {
                tracing::info!("purged {n} expired private-state trash entries");
            }
        }
        m.tmux.ensure_server().await;
        m.sync_from_config().await;
        m
    }

    fn save_cfg(&self, cfg: &Config) -> Result<()> {
        cfg.save(&self.cfg_path)?;
        *self.cfg_mtime.lock().unwrap() = disk_mtime(&self.cfg_path);
        if let Err(e) = crate::endpoint::update_token(&cfg.daemon.token) {
            tracing::warn!("config saved but endpoint descriptor was not updated: {e:#}");
        }
        Ok(())
    }

    fn save_cfg_text(&self, cfg: &Config, text: &str) -> Result<()> {
        Config::save_text(&self.cfg_path, text)?;
        *self.cfg_mtime.lock().unwrap() = disk_mtime(&self.cfg_path);
        if let Err(e) = crate::endpoint::update_token(&cfg.daemon.token) {
            tracing::warn!("config saved but endpoint descriptor was not updated: {e:#}");
        }
        Ok(())
    }

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
        if let Err(e) = crate::endpoint::update_token(&self.cfg.read().await.daemon.token) {
            tracing::warn!("config reloaded but endpoint descriptor was not updated: {e:#}");
        }
        self.sync_from_config().await;
        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });
        self.announce_projects().await;
        self.announce_shortcuts().await;
        true
    }

    async fn reload_if_due(self: &Arc<Self>) {
        let now = now_ms();
        let last = self.cfg_checked.load(Ordering::Relaxed);
        if now.saturating_sub(last) < CFG_CHECK_MS {
            return;
        }
        if self
            .cfg_checked
            .compare_exchange(last, now, Ordering::Relaxed, Ordering::Relaxed)
            .is_err()
        {
            return;
        }
        self.reload_presets_if_changed().await;
        self.reload_jukebox_if_changed().await;
        self.reload_if_changed().await;
    }

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

    pub async fn reload_jukebox_if_changed(self: &Arc<Self>) -> bool {
        let disk = crate::jukebox::dir_stamp();
        {
            let mut seen = self.jukebox_mtime.lock().unwrap();
            if *seen == disk {
                return false;
            }
            *seen = disk;
        }
        tracing::info!("jukebox definitions changed on disk, reloading");
        crate::jukebox::reload();
        let _ = self.events.send(Event::Jukebox {
            jukebox: crate::jukebox::catalog(),
        });
        true
    }

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

        let _ = self.events.send(Event::Quit);
        let watch = self.config().await.daemon.game_cmd;

        tokio::spawn(async move {
            tokio::time::sleep(Duration::from_millis(delay_ms.min(60_000))).await;

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

    pub fn watching(self: &Arc<Self>, name: &str) -> WatchGuard {
        if let Ok(mut w) = self.watchers.lock() {
            *w.entry(name.to_string()).or_insert(0) += 1;
        }
        WatchGuard(self.clone(), name.to_string())
    }

    fn watched(&self, name: &str) -> bool {
        self.watchers
            .lock()
            .map(|w| w.contains_key(name))
            .unwrap_or(true)
    }

    pub async fn config(&self) -> Config {
        self.cfg.read().await.clone()
    }

    pub async fn resolve_cap(&self, presented: Option<&str>) -> Option<crate::grant::Cap> {
        let root = self.cfg.read().await.daemon.token.clone();
        self.grants.read().await.resolve(presented, &root)
    }

    pub async fn cap_ok(
        &self,
        cap: &crate::grant::Cap,
        session: &str,
        need: crate::grant::Level,
    ) -> bool {
        let is_host = self
            .live
            .read()
            .await
            .get(session)
            .map(|l| l.host)
            .unwrap_or(false);
        cap.allows(session, is_host, need)
    }

    pub async fn session_known(&self, name: &str) -> bool {
        self.live.read().await.contains_key(name) || self.config().await.session(name).is_some()
    }

    pub async fn mint_grant(
        &self,
        grantor: String,
        sessions: Vec<String>,
        level: crate::grant::Level,
    ) -> Result<String> {
        if !self.session_known(&grantor).await {
            bail!("no such session to grant to: {grantor}");
        }
        let live = self.live.read().await;
        let mut scope = std::collections::HashSet::new();
        for s in sessions {
            if live.get(&s).map(|l| l.host).unwrap_or(false) {
                bail!("the host terminal cannot be granted: {s}");
            }
            if !live.contains_key(&s) && self.config().await.session(&s).is_none() {
                bail!("no such session to grant: {s}");
            }
            scope.insert(s);
        }
        drop(live);
        Ok(self.grants.write().await.mint(crate::grant::Grant {
            grantor,
            sessions: scope,
            level,
        }))
    }

    pub async fn revoke_grants(&self, grantor: &str) {
        self.grants.write().await.revoke_grantor(grantor);
    }

    pub async fn grant_count(&self) -> usize {
        self.grants.read().await.count()
    }

    pub fn create_task(
        &self,
        from: String,
        to: String,
        body: String,
    ) -> Result<crate::tasks::Task> {
        self.tasks.lock().unwrap().create(from, to, body)
    }

    pub fn tasks_for(&self, who: &str) -> Vec<crate::tasks::Task> {
        self.tasks.lock().unwrap().visible(who)
    }

    pub fn task_for(&self, who: &str, id: &str) -> Option<crate::tasks::Task> {
        self.tasks.lock().unwrap().get(who, id)
    }

    pub fn update_task(
        &self,
        who: &str,
        id: &str,
        status: crate::tasks::Status,
        note: Option<String>,
    ) -> Result<crate::tasks::Task> {
        self.tasks.lock().unwrap().update(who, id, status, note)
    }

    pub fn remove_task(&self, who: &str, id: &str, force: bool) -> Result<crate::tasks::Task> {
        self.tasks.lock().unwrap().remove(who, id, force)
    }

    pub fn prune_tasks(&self, who: &str, all: bool) -> Result<usize> {
        self.tasks.lock().unwrap().prune(who, all)
    }

    pub async fn usage(&self) -> crate::usage::Snapshot {
        self.usage.read().await.clone()
    }

    pub async fn set_usage(&self, snap: crate::usage::Snapshot) {
        {
            let mut cur = self.usage.write().await;
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

        live.retain(|name, l| l.ephemeral || cfg.session(name).is_some());

        for s in &cfg.sessions {
            live.entry(s.name.clone())
                .and_modify(|l| {
                    l.cfg = s.clone();
                    if !s.breadcrumb_yolo {
                        l.breadcrumbs_pending = false;
                    }
                })
                .or_insert(Live {
                    cfg: s.clone(),
                    ephemeral: false,
                    host: false,
                    state: State::Down,
                    seq: 0,
                    retick_seq: 0,
                    hash: 0,
                    last_change: 0,
                    state_since: 0,
                    bell: false,
                    cols: BOOT_COLS,
                    rows: BOOT_ROWS,
                    plain: String::new(),
                    screen: None,
                    emu: None,
                    reader: None,
                    input: None,
                    breadcrumbs: Vec::new(),
                    breadcrumbs_pending: false,
                    title: TitleCapture::default(),
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

        let mut adopted = false;
        for name in self.tmux.list().await {
            if !self.live.read().await.contains_key(&name) {
                adopted |= self.adopt(&name).await;
            }
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
                let m = self.clone();
                let name = name.clone();
                tokio::spawn(async move { m.nudge_redraw(&name).await });
            }
        }

        if adopted {
            let _ = self.events.send(Event::Sessions {
                sessions: self.views().await,
            });
        }
    }

    async fn nudge_redraw(self: &Arc<Self>, name: &str) {
        // A capture cannot restore terminal modes; SIGWINCH makes TUIs reassert them.
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
                host: false,
                state: State::Working,
                seq: 0,
                retick_seq: 0,
                hash: 0,
                last_change: now_ms(),
                state_since: now_ms(),
                bell: false,
                cols: BOOT_COLS,
                rows: BOOT_ROWS,
                plain: String::new(),
                screen: None,
                emu: None,
                reader: None,
                input: None,
                breadcrumbs: Vec::new(),
                breadcrumbs_pending: false,
                title: TitleCapture::default(),
            },
        );
        true
    }

    async fn session_cfg(&self, name: &str) -> Option<SessionCfg> {
        if let Some(s) = self.cfg.read().await.session(name) {
            return Some(s.clone());
        }
        self.live.read().await.get(name).map(|l| l.cfg.clone())
    }

    async fn project_for(&self, cfg: &Config, s: &SessionCfg) -> Option<ProjectCfg> {
        if let Some(p) = cfg.project_of(s) {
            return Some(p.clone());
        }
        self.temp.read().await.get(&s.project).cloned()
    }

    pub async fn start(self: &Arc<Self>, name: &str) -> Result<()> {
        self.ensure_state_id(name).await?;
        let cfg = self.config().await;
        let configured = cfg.session(name).cloned();
        let s = match configured.as_ref() {
            Some(s) => s.clone(),
            None => self
                .session_cfg(name)
                .await
                .ok_or_else(|| anyhow!("no such session: {name}"))?,
        };

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
        cfg.network_of(&s, &p)?;

        if self.tmux.exists(name).await {
            bail!("session {name} is already running");
        }
        if cfg.command_of(&s).trim().is_empty() {
            bail!(
                "session {name} names command preset {:?}, which has no file",
                s.command
            );
        }
        let dir = expand(&p.dir);
        if p.temp {
            std::fs::create_dir_all(&dir).with_context(|| format!("making {dir}"))?;
        }
        if !std::path::Path::new(&dir).is_dir() {
            bail!("{dir} is not a directory");
        }
        if let Some(what) = crate::sandbox::refused(&dir) {
            bail!("project {} cannot live at {dir}: it reaches {what}", p.name);
        }

        let (cols, rows, host) = match self.live.read().await.get(name) {
            Some(l) => (l.cols, l.rows, l.host),
            None => (BOOT_COLS, BOOT_ROWS, false),
        };
        let argv = if host {
            crate::sandbox::host_argv(&cfg, &s, &p)
        } else {
            crate::sandbox::prepare_private(&cfg, &s, &p)?;
            build_argv(&cfg, &s, &p)?
        };
        tracing::info!("starting {name}: {}", argv.join(" "));
        self.tmux.spawn(name, &dir, cols, rows, &argv).await?;
        self.spawn_reader(name).await;
        let command = cfg.command_of(&s);
        let directory = expand(&p.dir);
        let vars = TemplateVars {
            agent: &s.name,
            project: &p.name,
            directory: &directory,
            command: &command,
        };
        let crumbs: Vec<String> = cfg
            .breadcrumbs_of(&s, &p)
            .into_iter()
            .map(|text| render_template_with(&text, &[], Some(&vars)))
            .collect();
        let mut live = self.live.write().await;
        if let Some(l) = live.get_mut(name) {
            l.title = TitleCapture::default();
            l.breadcrumbs.clear();
            l.breadcrumbs_pending = false;
            if !host && s.breadcrumb_yolo && !crumbs.is_empty() {
                l.breadcrumbs = breadcrumb_block(&crumbs).into_bytes();
                l.breadcrumbs_pending = true;
            }
        }
        Ok(())
    }

    pub async fn stop(self: &Arc<Self>, name: &str) -> Result<()> {
        if self.tmux.exists(name).await {
            self.tmux.kill(name).await?;
        }
        if self.is_ephemeral(name).await {
            self.forget(name).await;
            return Ok(());
        }
        if let Some(l) = self.live.write().await.get_mut(name) {
            l.set_state(State::Down);
            l.screen = None;
            l.emu = None;
            // A stopped run must not leave its generated task title on the downed
            // session. Resetting the capture also makes late title responses from
            // this run stale before the next start.
            l.title = TitleCapture::default();
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

    async fn forget(self: &Arc<Self>, name: &str) {
        let (handle, project, session) = {
            let mut live = self.live.write().await;
            match live.remove(name) {
                Some(mut l) => (l.reader.take(), l.cfg.project.clone(), l.cfg),
                None => return,
            }
        };
        if let Err(e) = crate::sandbox::remove_ephemeral_state(&session) {
            tracing::warn!("removing temporary private state for {name}: {e:#}");
        }
        self.temp.write().await.remove(&project);
        self.grants.write().await.revoke_grantor(name);
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
        check_breadcrumbs(&cfg, &p.breadcrumbs)?;
        if cfg.project(&p.name).is_some() {
            bail!("project {} already exists", p.name);
        }
        cfg.projects.push(p);
        self.save_cfg(&cfg)?;
        drop(cfg);
        self.announce_projects().await;
        Ok(())
    }

    pub async fn update_project(self: &Arc<Self>, name: &str, mut p: ProjectCfg) -> Result<()> {
        self.reload_if_changed().await;
        settle(&mut p);
        check_project(&p)?;

        let mut cfg = self.cfg.write().await;
        check_breadcrumbs(&cfg, &p.breadcrumbs)?;
        let idx = cfg
            .projects
            .iter()
            .position(|x| x.name == name)
            .ok_or_else(|| anyhow!("no such project: {name}"))?;
        if p.name != name && cfg.project(&p.name).is_some() {
            bail!("project {} already exists", p.name);
        }
        for s in cfg.sessions.iter().filter(|s| s.project == name) {
            cfg.network_of(s, &p).with_context(|| {
                format!(
                    "project {} cannot lower its network ceiling below agent {}",
                    p.name, s.name
                )
            })?;
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
        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });
        Ok(())
    }

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
        self.cfg.read().await.shortcuts_all()
    }

    async fn announce_shortcuts(&self) {
        let _ = self.events.send(Event::Shortcuts {
            shortcuts: self.shortcuts().await,
        });
    }

    pub async fn add_shortcut(self: &Arc<Self>, mut sc: ShortcutCfg) -> Result<()> {
        self.reload_if_changed().await;
        let mut cfg = self.cfg.write().await;
        sc.builtin = false;
        check_shortcut(&cfg, &sc)?;
        if cfg
            .shortcuts
            .iter()
            .any(|existing| existing.name == sc.name)
        {
            bail!("shortcut {} already exists", sc.name);
        }
        let attach_project = (sc.kind == ShortcutKind::Breadcrumb && !sc.project.trim().is_empty())
            .then(|| sc.project.clone());
        cfg.shortcuts.push(sc.clone());
        if let Some(project) = attach_project {
            if let Some(p) = cfg.projects.iter_mut().find(|p| p.name == project) {
                if !p.breadcrumbs.contains(&sc.name) {
                    p.breadcrumbs.push(sc.name.clone());
                }
            }
        }
        self.save_cfg(&cfg)?;
        drop(cfg);
        self.announce_shortcuts().await;
        Ok(())
    }

    pub async fn update_shortcut(self: &Arc<Self>, name: &str, mut sc: ShortcutCfg) -> Result<()> {
        self.reload_if_changed().await;
        let mut cfg = self.cfg.write().await;
        sc.builtin = false;
        if cfg.is_builtin_shortcut(name) {
            bail!("shortcut {name} is built in and cannot be edited");
        }
        check_shortcut(&cfg, &sc)?;
        let idx = cfg
            .shortcuts
            .iter()
            .position(|x| x.name == name)
            .ok_or_else(|| anyhow!("no such shortcut: {name}"))?;
        if sc.name != name
            && cfg
                .shortcuts
                .iter()
                .any(|existing| existing.name == sc.name)
        {
            bail!("shortcut {} already exists", sc.name);
        }
        let old = cfg.shortcuts[idx].clone();
        if sc.name != name {
            for p in &mut cfg.projects {
                for attached in &mut p.breadcrumbs {
                    if attached == name {
                        *attached = sc.name.clone();
                    }
                }
            }
            for s in &mut cfg.sessions {
                for attached in &mut s.breadcrumbs {
                    if attached == name {
                        *attached = sc.name.clone();
                    }
                }
            }
        }
        cfg.shortcuts[idx] = sc.clone();
        if sc.kind == ShortcutKind::Breadcrumb {
            if !old.project.trim().is_empty() && old.project != sc.project {
                if let Some(p) = cfg.projects.iter_mut().find(|p| p.name == old.project) {
                    p.breadcrumbs.retain(|b| b != &sc.name);
                }
            }
            if !sc.project.trim().is_empty() {
                if let Some(p) = cfg.projects.iter_mut().find(|p| p.name == sc.project) {
                    if !p.breadcrumbs.contains(&sc.name) {
                        p.breadcrumbs.push(sc.name.clone());
                    }
                }
            }
        } else {
            for p in &mut cfg.projects {
                p.breadcrumbs.retain(|b| b != name && b != &sc.name);
            }
            for s in &mut cfg.sessions {
                s.breadcrumbs.retain(|b| b != name && b != &sc.name);
            }
        }
        self.save_cfg(&cfg)?;
        drop(cfg);
        self.announce_projects().await;
        self.announce_shortcuts().await;
        Ok(())
    }

    pub async fn remove_shortcut(self: &Arc<Self>, name: &str) -> Result<()> {
        self.reload_if_changed().await;
        let mut cfg = self.cfg.write().await;
        if cfg.is_builtin_shortcut(name) {
            bail!("shortcut {name} is built in and cannot be deleted");
        }
        let Some(sc) = cfg.shortcut(name) else {
            bail!("no such shortcut: {name}");
        };
        if sc.kind == ShortcutKind::Breadcrumb
            && (cfg
                .projects
                .iter()
                .any(|p| p.breadcrumbs.iter().any(|b| b == name))
                || cfg
                    .sessions
                    .iter()
                    .any(|s| s.breadcrumbs.iter().any(|b| b == name)))
        {
            bail!("breadcrumb {name} is still attached to a project or agent");
        }
        cfg.shortcuts.retain(|s| s.name != name);
        self.save_cfg(&cfg)?;
        drop(cfg);
        self.announce_shortcuts().await;
        Ok(())
    }

    pub async fn run_shortcut(self: &Arc<Self>, name: &str, want: RunWhere) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self.config().await;
        let sc = cfg
            .shortcut(name)
            .ok_or_else(|| anyhow!("no such shortcut: {name}"))?
            .clone();
        check_shortcut(&cfg, &sc)?;
        if matches!(sc.kind, ShortcutKind::Breadcrumb | ShortcutKind::FileAction) {
            bail!("shortcut {} is not runnable as an agent errand", sc.name);
        }
        drop(cfg);
        self.run_errand(sc, want, false).await
    }

    pub async fn file_action(
        self: &Arc<Self>,
        project: &str,
        raw_path: &str,
        command: &str,
    ) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self.config().await;
        let p = cfg
            .project(project.trim())
            .cloned()
            .ok_or_else(|| anyhow!("no such project: {project}"))?;
        let path = project_action_path(&p, raw_path)?;
        if command.trim().is_empty() {
            bail!("file action has no command");
        }
        let command = normalize_action_command(raw_path, &path, command);

        let s = SessionCfg {
            name: "file-action".into(),
            project: p.name.clone(),
            command: cfg.defaults.shell.clone(),
            cmd: Some(command),
            ..Default::default()
        };

        let result = async {
            crate::sandbox::prepare_private(&cfg, &s, &p)?;
            let argv = build_argv(&cfg, &s, &p)?;
            let mut child = Command::new(&argv[0])
                .args(&argv[1..])
                .kill_on_drop(true)
                .stdin(Stdio::null())
                .stdout(Stdio::piped())
                .stderr(Stdio::piped())
                .spawn()
                .context("starting file action")?;
            let stdout = child
                .stdout
                .take()
                .context("capturing file action stdout")?;
            let stderr = child
                .stderr
                .take()
                .context("capturing file action stderr")?;

            let collected = tokio::time::timeout(FILE_ACTION_TIMEOUT, async {
                let stdout = read_action_output(stdout);
                let stderr = read_action_output(stderr);
                let status = child.wait();
                let (stdout, stderr, status) = tokio::join!(stdout, stderr, status);
                Ok::<_, anyhow::Error>((stdout?, stderr?, status?))
            })
            .await;

            let ((stdout, stdout_truncated), (stderr, stderr_truncated), status) = match collected {
                Ok(result) => result?,
                Err(_) => {
                    let _ = child.kill().await;
                    let _ = child.wait().await;
                    bail!("file action timed out")
                }
            };

            let mut text = String::from_utf8_lossy(&stdout).into_owned();
            if !stderr.is_empty() {
                if !text.is_empty() && !text.ends_with('\n') {
                    text.push('\n');
                }
                text.push_str(&String::from_utf8_lossy(&stderr));
            }
            if stdout_truncated || stderr_truncated {
                text.push_str("\n[output truncated]");
            }
            if !status.success() {
                bail!("file action failed: {}", text.trim());
            }
            Ok::<_, anyhow::Error>(if text.trim().is_empty() {
                "(no output)".into()
            } else {
                text.trim_end().into()
            })
        }
        .await;

        if let Err(e) = crate::sandbox::remove_ephemeral_state(&s) {
            tracing::warn!("removing file action private state: {e:#}");
        }
        result
    }

    pub async fn file_action_command(
        self: &Arc<Self>,
        project: &str,
        raw_path: &str,
        command: &str,
    ) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self.config().await;
        let p = cfg
            .project(project.trim())
            .cloned()
            .ok_or_else(|| anyhow!("no such project: {project}"))?;
        let path = project_action_path(&p, raw_path)?;
        Ok(normalize_action_command(raw_path, &path, command))
    }

    pub async fn run_errand(
        self: &Arc<Self>,
        sc: ShortcutCfg,
        want: RunWhere,
        host: bool,
    ) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self.config().await;
        let name = sc.name.as_str();
        if matches!(sc.kind, ShortcutKind::Breadcrumb | ShortcutKind::FileAction) {
            bail!("shortcut {name} is not runnable as an agent errand");
        }

        let asked = match want.project.as_deref().map(str::trim) {
            Some(p) if !p.is_empty() => Some(p.to_string()),
            _ => None,
        };
        let fresh = want.temp || (asked.is_none() && sc.link == ShortcutLink::Temp);
        let named = match (&asked, sc.link) {
            _ if fresh => String::new(),
            (Some(p), _) => p.clone(),
            (None, ShortcutLink::Ask) => {
                bail!(
                    "shortcut {name} asks where to run; name a project or ask for a temporary one"
                )
            }
            (None, _) => sc.project.clone(),
        };
        if !fresh && cfg.project(&named).is_none() {
            bail!("no such project: {named}");
        }
        let template = cfg.project(&sc.project).cloned().unwrap_or_default();

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
                    cfg: cfg.session_for(&sc, name.clone(), project),
                    ephemeral: true,
                    host,
                    state: State::Down,
                    seq: 0,
                    retick_seq: 0,
                    hash: 0,
                    last_change: 0,
                    state_since: 0,
                    bell: false,
                    cols: BOOT_COLS,
                    rows: BOOT_ROWS,
                    plain: String::new(),
                    screen: None,
                    emu: None,
                    reader: None,
                    input: None,
                    breadcrumbs: Vec::new(),
                    breadcrumbs_pending: false,
                    title: TitleCapture::default(),
                },
            );
            name
        };

        if let Err(e) = self.start(&session).await {
            self.forget(&session).await;
            return Err(e);
        }

        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });

        let text = render_template(&sc.text, &want.random_tips);
        if !text.trim().is_empty() {
            let m = self.clone();
            let target = session.clone();
            let tips = want.random_tips.clone();
            tokio::spawn(async move { m.deliver(&target, &text, tips).await });
        }

        Ok(session)
    }

    async fn deliver(self: &Arc<Self>, name: &str, text: &str, random_tips: Vec<String>) {
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
        self.send_keys(name, vec!["Enter".into()], false, random_tips)
            .await;
    }

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

    pub async fn add(self: &Arc<Self>, mut s: SessionCfg) -> Result<()> {
        self.reload_if_changed().await;
        let mut cfg = self.cfg.write().await;
        if cfg.session(&s.name).is_some() {
            bail!("session {} already exists", s.name);
        }
        check_name(&s.name)?;
        check_belongs(&cfg, &s)?;
        s.limits.validate()?;
        // A client has no authority over which durable state an agent receives.  Always mint a
        // fresh key, including if a hand-written request carried a stale one.
        s.state_id = uuid::Uuid::new_v4().to_string();
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

    pub async fn update(self: &Arc<Self>, name: &str, mut s: SessionCfg) -> Result<()> {
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
        s.limits.validate()?;
        let idx = cfg
            .sessions
            .iter()
            .position(|x| x.name == name)
            .ok_or_else(|| anyhow!("no such session: {name}"))?;
        // Keep private state with the agent across every edit, particularly a rename.  The wire
        // deliberately does not expose this field, but also must not be able to change it.
        s.state_id = cfg.sessions[idx].state_id.clone();
        cfg.sessions[idx] = s.clone();
        self.save_cfg(&cfg)?;
        drop(cfg);

        if renamed {
            self.readopt(name, &s.name).await;
        }
        self.sync_from_config().await;
        Ok(())
    }

    async fn readopt(self: &Arc<Self>, old: &str, new: &str) {
        // The control reader is attached by tmux name; renaming requires a fresh capture/emulator.
        let running = {
            let mut live = self.live.write().await;
            let mut l = match live.remove(old) {
                Some(l) => l,
                None => return,
            };
            if let Some(h) = l.reader.take() {
                h.abort();
            }
            // The input consumer captures the tmux target when it is spawned. Drop its
            // sender so the next key creates a consumer addressed to the new name; keeping
            // it would silently route every later key to the vanished old session.
            l.input = None;
            let running = l.emu.take().is_some();
            l.cfg.name = new.to_string();
            live.insert(new.to_string(), l);
            running
        };
        if running && self.spawn_reader(new).await {
            let m = self.clone();
            let name = new.to_string();
            tokio::spawn(async move { m.nudge_redraw(&name).await });
        }
    }

    pub async fn remove(self: &Arc<Self>, name: &str) -> Result<()> {
        self.reload_if_changed().await;
        if self.is_ephemeral(name).await {
            return self.stop(name).await;
        }
        self.ensure_state_id(name).await?;
        self.stop(name).await.ok();
        let mut cfg = self.cfg.write().await;
        let session = cfg
            .session(name)
            .cloned()
            .ok_or_else(|| anyhow!("no such session: {name}"))?;
        let trashed = crate::sandbox::trash_state(&session, name)?;
        cfg.sessions.retain(|s| s.name != name);
        if let Err(e) = self.save_cfg(&cfg) {
            if let Some(path) = trashed.as_deref() {
                if let Err(restore) = crate::sandbox::restore_trashed_state(&session, path) {
                    tracing::error!("config delete failed: {e:#}; private-state restore also failed: {restore:#}");
                }
            }
            return Err(e);
        }
        drop(cfg);
        if let Err(e) = crate::sandbox::purge_trash() {
            tracing::warn!("purging private-state trash: {e:#}");
        }
        self.sync_from_config().await;
        Ok(())
    }

    /// Stop an agent and discard only its private tool state.  The old tree is recoverable in
    /// the daemon-owned trash for two weeks; the configured agent remains and reseeds on start.
    pub async fn reset_state(self: &Arc<Self>, name: &str) -> Result<()> {
        self.reload_if_changed().await;
        if self.is_ephemeral(name).await {
            bail!("temporary session {name} has no resettable private state");
        }
        self.ensure_state_id(name).await?;
        self.stop(name).await.ok();
        let cfg = self.config().await;
        let session = cfg
            .session(name)
            .cloned()
            .ok_or_else(|| anyhow!("no such session: {name}"))?;
        crate::sandbox::trash_state(&session, &format!("{name}-reset"))?;
        if let Err(e) = crate::sandbox::purge_trash() {
            tracing::warn!("purging private-state trash: {e:#}");
        }
        self.sync_from_config().await;
        Ok(())
    }

    pub async fn stored_states(&self) -> Result<Vec<crate::sandbox::StoredState>> {
        let sessions = self.config().await.sessions;
        tokio::task::spawn_blocking(move || crate::sandbox::stored_states(&sessions))
            .await
            .map_err(|e| anyhow!("scanning private state: {e}"))
    }

    pub async fn delete_stored_state(&self, kind: &str, key: &str) -> Result<()> {
        let sessions = self.config().await.sessions;
        let kind = kind.to_string();
        let key = key.to_string();
        tokio::task::spawn_blocking(move || {
            crate::sandbox::delete_stored_state(&kind, &key, &sessions)
        })
        .await
        .map_err(|e| anyhow!("deleting private state: {e}"))?
    }

    pub async fn restore_stored_state(self: &Arc<Self>, key: &str) -> Result<String> {
        self.reload_if_changed().await;
        let archived = crate::sandbox::trashed_session(key)?;
        let mut cfg = self.cfg.write().await;
        let existing = cfg
            .sessions
            .iter()
            .find(|s| !archived.state_id.is_empty() && s.state_id == archived.state_id)
            .cloned();
        let add = existing.is_none();
        let session = existing.unwrap_or_else(|| archived.clone());
        if add {
            if cfg.session(&session.name).is_some() {
                bail!(
                    "session {:?} already exists with different private state",
                    session.name
                );
            }
            check_name(&session.name)?;
            check_belongs(&cfg, &session)?;
        }

        crate::sandbox::restore_stored_state(key, &cfg.sessions)?;
        if add {
            cfg.sessions.push(session.clone());
            if let Err(e) = self.save_cfg(&cfg) {
                if let Err(rollback) = crate::sandbox::rollback_restored_state(key, &session) {
                    tracing::error!("restored-agent config save failed: {e:#}; state rollback also failed: {rollback:#}");
                }
                return Err(e);
            }
        }
        crate::sandbox::finish_restored_state(&session);
        drop(cfg);
        self.sync_from_config().await;
        Ok(session.name)
    }

    /// Fill in a config entry written before state identities existed. It deliberately receives
    /// a fresh tree; any unclaimed name-keyed directory is an orphan in the storage inventory.
    async fn ensure_state_id(&self, name: &str) -> Result<()> {
        let mut cfg = self.cfg.write().await;
        let Some(idx) = cfg.sessions.iter().position(|s| s.name == name) else {
            return Ok(()); // temporary/session-adopted agent: its in-memory Default owns a key.
        };
        if cfg.sessions[idx].state_id.is_empty() {
            cfg.sessions[idx].state_id = uuid::Uuid::new_v4().to_string();
            self.save_cfg(&cfg)?;
        }
        Ok(())
    }

    pub async fn patch_config(self: &Arc<Self>, patch: Value) -> Result<()> {
        self.reload_if_changed().await;

        let text = std::fs::read_to_string(&self.cfg_path)
            .with_context(|| format!("reading {}", self.cfg_path.display()))?;
        let mut document: toml::Value = toml::from_str(&text).context("parsing config.toml")?;
        let patch = json_to_toml(patch)?;
        if !patch.is_table() {
            bail!("config patch must be a JSON object");
        }
        merge_toml(&mut document, patch);

        let old_token = self.cfg.read().await.daemon.token.clone();
        let mut new = Config::parse(&toml::to_string_pretty(&document)?)?;
        if new.daemon.token == crate::config::TOKEN_REDACTED {
            new.daemon.token = old_token.clone();
            if let Some(daemon) = document
                .get_mut("daemon")
                .and_then(toml::Value::as_table_mut)
            {
                daemon.insert("token".into(), toml::Value::String(old_token));
            }
        }
        validate_config(&new)?;

        self.save_cfg_text(&new, &toml::to_string_pretty(&document)?)?;

        *self.rules.write().await = compile_rules(&new);
        *self.cfg.write().await = new;
        self.sync_from_config().await;
        self.announce_projects().await;
        self.announce_shortcuts().await;
        Ok(())
    }

    pub async fn replace_config(self: &Arc<Self>, text: &str) -> Result<()> {
        let mut new = Config::parse(text)?;
        if new.daemon.token == crate::config::TOKEN_REDACTED {
            new.daemon.token = self.cfg.read().await.daemon.token.clone();
        }
        validate_config(&new)?;
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
                    command_preset: cfg.command_name(&l.cfg),
                    cmd: l.cfg.cmd.clone(),
                    sandbox: l.cfg.sandbox.clone(),
                    breadcrumbs: l.cfg.breadcrumbs.clone(),
                    breadcrumb_yolo: l.cfg.breadcrumb_yolo,
                    breadcrumbs_pending: l.breadcrumbs_pending,
                    agent: cfg.command_of(&l.cfg),
                    state: l.state,
                    alive: l.state != State::Down,
                    cols: l.cols,
                    rows: l.rows,
                    network: p
                        .map(|p| cfg.network_of(&l.cfg, p).unwrap_or(p.network))
                        .unwrap_or_default(),
                    network_override: l.cfg.network,
                    limits: p.map(|p| cfg.limits_of(&l.cfg, p)).unwrap_or(l.cfg.limits),
                    limits_override: l.cfg.limits,
                    autostart: l.cfg.autostart,
                    ephemeral: l.ephemeral,
                    last_change: l.last_change,
                    state_since: l.state_since,
                    title: l
                        .title
                        .override_title
                        .clone()
                        .or_else(|| l.screen.as_ref().map(|s| s.title.clone()))
                        .unwrap_or_default(),
                    bell: l.bell,
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

    pub async fn clear_bell(self: &Arc<Self>, name: &str) {
        {
            let mut live = self.live.write().await;
            match live.get_mut(name) {
                Some(l) if l.bell => l.bell = false,
                _ => return,
            }
        }
        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });
    }

    async fn queue_input(&self, name: &str, item: Input) {
        // One consumer preserves ordering across keys, mouse reports, replies, and paste.
        let mut spawn_rx = None;
        let mut item = Some(item);
        {
            let mut live = self.live.write().await;
            if let Some(l) = live.get_mut(name) {
                if l.input.as_ref().is_some_and(|tx| tx.is_closed()) {
                    l.input = None;
                }
                if l.input.is_none() {
                    let (tx, rx) = mpsc::unbounded_channel();
                    l.input = Some(tx);
                    spawn_rx = Some(rx);
                }
                if let Some(tx) = l.input.as_ref() {
                    if let Some(i) = item.take() {
                        if let Err(e) = tx.send(i) {
                            item = Some(e.0);
                        }
                    }
                }
            }
        }
        if let Some(rx) = spawn_rx {
            let tmux = self.tmux.clone();
            let name = name.to_string();
            tokio::spawn(async move { Self::run_input(tmux, name, rx).await });
        }
        if let Some(item) = item {
            Self::send_input(&self.tmux, name, item).await;
        }
    }

    async fn run_input(tmux: Tmux, name: String, mut rx: mpsc::UnboundedReceiver<Input>) {
        while let Some(first) = rx.recv().await {
            let mut batch = vec![first];
            while let Ok(next) = rx.try_recv() {
                batch.push(next);
            }
            crate::perf::PERF.drains.fetch_add(1, Ordering::Relaxed);
            crate::perf::PERF
                .drained
                .fetch_add(batch.len() as u64, Ordering::Relaxed);
            let batch = merge_input(batch);
            for item in batch {
                Self::send_input(&tmux, &name, item).await;
            }
        }
    }

    async fn send_input(tmux: &Tmux, name: &str, item: Input) {
        let started = Instant::now();
        let (what, res) = match item {
            Input::Keys { keys, literal } => ("keys", tmux.send_keys(name, &keys, literal).await),
            Input::Bytes(b) => ("bytes", tmux.send_bytes(name, &b).await),
            Input::Paste(b) => ("paste", tmux.paste_bytes(name, &b).await),
            Input::Gap(d) => {
                tokio::time::sleep(d).await;
                return;
            }
        };
        crate::perf::PERF.send.add(started.elapsed());
        if let Err(e) = res {
            if what == "paste" {
                tracing::warn!("paste to {name}: {e:#}");
            } else {
                tracing::debug!("{what} to {name}: {e:#}");
            }
        }
    }

    async fn capture_title_keys(self: &Arc<Self>, name: &str, keys: &[String], literal: bool) {
        let cfg = self.config().await;

        let mut request = None;
        let mut announce = false;
        {
            let mut live = self.live.write().await;
            let Some(l) = live.get_mut(name) else { return };
            let Some(agent) = title_agent(&cfg, &l.cfg) else {
                return;
            };
            let (policy, model) = title_settings(&cfg, agent);
            if policy == TitlePolicy::Never {
                return;
            }

            let mut submission = None;
            if literal {
                for key in keys {
                    l.title.composer.literal(key);
                }
            } else {
                for key in keys {
                    if let Some(s) = l.title.composer.key(key) {
                        submission = Some(s);
                    }
                }
            }

            match submission {
                Some(Submission::New(native)) => {
                    l.title.conversation = l.title.conversation.wrapping_add(1);
                    l.title.generation = l.title.generation.wrapping_add(1);
                    l.title.pending = false;
                    l.title.override_title = native;
                    announce = true;
                }
                Some(Submission::Prompt(prompt))
                    if l.state == State::Waiting && is_dialog_answer(&prompt) => {}
                Some(Submission::Prompt(prompt)) => {
                    let armed = match policy {
                        TitlePolicy::Never => false,
                        TitlePolicy::Once => l.title.override_title.is_none() && !l.title.pending,
                        TitlePolicy::Always => true,
                    };
                    if armed {
                        l.title.generation = l.title.generation.wrapping_add(1);
                        l.title.pending = true;
                        request = Some(TitleRequest {
                            prompt,
                            conversation: l.title.conversation,
                            generation: l.title.generation,
                            key_file: cfg.daemon.openrouter_key_file.clone(),
                            model,
                        });
                    }
                }
                None => {}
            }
        }
        drop(cfg);

        if announce {
            let _ = self.events.send(Event::Sessions {
                sessions: self.views().await,
            });
        }
        if let Some(request) = request {
            let m = self.clone();
            let name = name.to_string();
            tokio::spawn(async move {
                let prompt = request.prompt.clone();
                let key_file = request.key_file.clone();
                let model = request.model.clone();
                let result = tokio::task::spawn_blocking(move || {
                    crate::title::summarize(&prompt, &key_file, &model)
                })
                .await;
                let result = match result {
                    Ok(r) => r,
                    Err(e) => Err(anyhow!("title worker: {e}")),
                };

                let mut moved = false;
                {
                    let mut live = m.live.write().await;
                    let Some(l) = live.get_mut(&name) else { return };
                    if l.title.conversation != request.conversation
                        || l.title.generation != request.generation
                    {
                        return;
                    }
                    l.title.pending = false;
                    match result {
                        Ok(title) => {
                            l.title.override_title = Some(title);
                            moved = true;
                        }
                        Err(e) => tracing::debug!("title for {name}: {e:#}"),
                    }
                }
                if moved {
                    let _ = m.events.send(Event::Sessions {
                        sessions: m.views().await,
                    });
                }
            });
        }
    }

    async fn capture_title_paste(&self, name: &str, text: &str) {
        let cfg = self.config().await;
        let mut live = self.live.write().await;
        let Some(l) = live.get_mut(name) else { return };
        let Some(agent) = title_agent(&cfg, &l.cfg) else {
            return;
        };
        if title_settings(&cfg, agent).0 != TitlePolicy::Never {
            l.title.composer.paste(text);
        }
    }

    pub async fn send_keys(
        self: &Arc<Self>,
        name: &str,
        keys: Vec<String>,
        literal: bool,
        random_tips: Vec<String>,
    ) {
        self.capture_title_keys(name, &keys, literal).await;
        let inject: Option<(Vec<u8>, usize)> = {
            let mut live = self.live.write().await;
            match live.get_mut(name) {
                None => None,
                Some(l) if !l.breadcrumbs_pending => None,
                Some(l) if !literal => keys.iter().position(|k| k == "Enter").map(|pos| {
                    l.breadcrumbs_pending = false;
                    let text = String::from_utf8_lossy(&l.breadcrumbs);
                    let text = render_template(&text, &random_tips);
                    (text.into_bytes(), pos)
                }),
                Some(_) => None,
            }
        };
        if let Some((text, pos)) = inject {
            if !text.is_empty() {
                if pos > 0 {
                    self.queue_input(
                        name,
                        Input::Keys {
                            keys: keys[..pos].to_vec(),
                            literal,
                        },
                    )
                    .await;
                }
                self.queue_input(name, Input::Paste(text)).await;
                self.queue_input(name, Input::Gap(Duration::from_millis(ENTER_GAP_MS)))
                    .await;
                if pos < keys.len() {
                    self.queue_input(
                        name,
                        Input::Keys {
                            keys: keys[pos..].to_vec(),
                            literal,
                        },
                    )
                    .await;
                }
                let _ = self.events.send(Event::Sessions {
                    sessions: self.views().await,
                });
                return;
            }
        }
        self.queue_input(name, Input::Keys { keys, literal }).await;
    }

    pub async fn send_mouse(&self, name: &str, ev: MouseInput, count: u8) {
        let single = {
            let live = self.live.read().await;
            live.get(name)
                .and_then(|l| l.emu.clone())
                .and_then(|e| e.lock().ok().and_then(|g| g.mouse_report(&ev)))
        };
        if let Some(single) = single {
            let count = count.max(1) as usize;
            let mut buf = Vec::with_capacity(single.len() * count);
            for _ in 0..count {
                buf.extend_from_slice(&single);
            }
            self.queue_input(name, Input::Bytes(buf)).await;
        }
    }

    pub async fn paste(&self, name: &str, text: &str) -> Result<()> {
        if !self.tmux.exists(name).await {
            bail!("session {name} is not running");
        }

        self.capture_title_paste(name, text).await;
        self.queue_input(name, Input::Paste(text.as_bytes().to_vec()))
            .await;
        Ok(())
    }

    /// Render a breadcrumb and paste it into this agent without submitting.
    pub async fn paste_breadcrumb(
        &self,
        name: &str,
        breadcrumb: &str,
        random_tips: Vec<String>,
    ) -> Result<()> {
        let cfg = self.config().await;
        let s = self
            .session_cfg(name)
            .await
            .ok_or_else(|| anyhow!("no such session: {name}"))?;
        let p = self
            .project_for(&cfg, &s)
            .await
            .ok_or_else(|| anyhow!("session {name} has no configured project"))?;
        let b = cfg
            .shortcut(breadcrumb)
            .filter(|b| b.kind == ShortcutKind::Breadcrumb)
            .ok_or_else(|| anyhow!("no such breadcrumb: {breadcrumb}"))?;
        let command = cfg.command_of(&s);
        let directory = expand(&p.dir);
        let vars = TemplateVars {
            agent: &s.name,
            project: &p.name,
            directory: &directory,
            command: &command,
        };
        let text = render_template_with(&b.text, &random_tips, Some(&vars));
        drop(cfg);
        if !self.tmux.exists(name).await {
            bail!("session {name} is not running");
        }
        self.queue_input(name, Input::Paste(text.into_bytes()))
            .await;
        Ok(())
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
        if let Some(state) = match_rules(&self.rules.read().await, text) {
            return state;
        }
        if changed || now_ms().saturating_sub(last_change) < IDLE_MS {
            State::Working
        } else {
            State::Idle
        }
    }

    pub async fn retick(self: &Arc<Self>) {
        self.reload_if_due().await;

        let now = now_ms();

        // Classification awaits the rules lock, so snapshot before releasing the live lock.
        let snapshot: Vec<(String, u64, String)> = {
            let live = self.live.read().await;
            live.iter()
                .filter(|(_, l)| l.state != State::Down)
                .filter_map(|(n, l)| {
                    let _ = l.screen.as_ref()?;
                    let idle_due = match l.state {
                        State::Working | State::Waiting => {
                            now.saturating_sub(l.state_since) >= IDLE_MS
                        }
                        State::Idle | State::Down => false,
                    };
                    if l.seq == l.retick_seq && !idle_due {
                        return None;
                    }
                    Some((n.clone(), l.last_change, l.plain.clone()))
                })
                .collect()
        };

        let mut dirty_list = false;
        for (name, last_change, plain) in snapshot {
            let state = self.classify(false, last_change, &plain).await;
            let mut live = self.live.write().await;
            if let Some(l) = live.get_mut(&name) {
                l.retick_seq = l.seq;
                if l.set_state(state) {
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
        let history = self.config().await.daemon.history_limit;
        if let Ok(cap) = self.tmux.capture(name, history).await {
            let mut seed = String::new();
            // tmux returns primary history with the alternate screen; seed each buffer separately.
            let visible = if cap.alt_screen {
                let split = cap.lines.len().saturating_sub(rows as usize);
                if split > 0 {
                    seed.push_str("\x1b[2J\x1b[H\x1b[0m");
                    seed.push_str(&cap.lines[..split].join("\r\n"));
                    seed.push_str("\r\n");
                }
                seed.push_str("\x1b[?1049h");
                &cap.lines[split..]
            } else {
                &cap.lines[..]
            };
            seed.push_str("\x1b[2J\x1b[H\x1b[0m");
            seed.push_str(&visible.join("\r\n"));
            seed.push_str(&format!("\x1b[{};{}H", cap.cy + 1, cap.cx + 1));
            if !cap.title.is_empty() {
                seed.push_str(&format!("\x1b]0;{}\x07", cap.title));
            }
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

        self.render_and_broadcast(name, &emu).await;
        true
    }

    async fn run_control(self: Arc<Self>, name: String, emu: Arc<Mutex<SessionEmu>>) {
        let (cols, rows) = match self.live.read().await.get(&name) {
            Some(l) => (l.cols, l.rows),
            None => {
                self.mark_down(&name).await;
                return;
            }
        };
        let (_child, master) = match self.tmux.control_attach(&name, cols, rows) {
            Ok(c) => c,
            Err(e) => {
                tracing::error!("control attach {name}: {e:#}");
                self.mark_down(&name).await;
                return;
            }
        };

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

        const FAST_TICK: Duration = Duration::from_millis(16);
        let slow_tick = Duration::from_millis(UNWATCHED_MS);
        let flush = tokio::time::sleep(FAST_TICK);
        tokio::pin!(flush);
        let mut dirty = false;
        let mut drawn: Option<Instant> = None;
        let copying = Arc::new(AtomicBool::new(false));

        loop {
            tokio::select! {
                line = rx.recv() => match line {
                    Some(l) => {
                        crate::perf::PERF.backlog.fetch_max(rx.len() as u64, Ordering::Relaxed);
                        crate::perf::PERF.lines.fetch_add(1, Ordering::Relaxed);
                        crate::perf::PERF.esc_bytes.fetch_add(l.len() as u64, Ordering::Relaxed);
                        if let Some(bytes) = parse_output(&l) {
                            crate::perf::PERF
                                .raw_bytes
                                .fetch_add(bytes.len() as u64, Ordering::Relaxed);
                            let replies = crate::perf::time(&crate::perf::PERF.feed, || {
                                if let Ok(mut e) = emu.lock() {
                                    e.feed(&bytes);
                                    e.take_replies()
                                } else {
                                    Vec::new()
                                }
                            });
                            if !replies.is_empty() {
                                let started = Instant::now();
                                self.queue_input(&name, Input::Bytes(replies)).await;
                                crate::perf::PERF.reply.add(started.elapsed());
                            }
                            dirty = true;
                        } else if l.starts_with(b"%exit") {
                            break;
                        }
                        // Output may pull an existing deadline forward, never postpone it.
                        let soon = tokio::time::Instant::now() + FAST_TICK;
                        if flush.deadline() > soon {
                            flush.as_mut().reset(soon);
                        }
                    }
                    None => break,
                },
                _ = flush.as_mut() => {
                    let watched = self.watched(&name);
                    flush.as_mut().reset(
                        tokio::time::Instant::now()
                            + if watched { FAST_TICK } else { slow_tick },
                    );

                    let due = watched
                        || drawn.is_none_or(|t| t.elapsed() >= Duration::from_millis(UNWATCHED_MS));
                    if dirty && due {
                        dirty = false;
                        drawn = Some(Instant::now());
                        self.render_and_broadcast(&name, &emu).await;
                    }
                    if watched && !copying.load(Ordering::Relaxed) {
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
            Ok(e) => crate::perf::time(&crate::perf::PERF.render, || e.render()),
            Err(_) => return,
        };
        let started = Instant::now();
        self.apply_frame(name, frame).await;
        crate::perf::PERF.apply.add(started.elapsed());
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
        let title_moved = meta.5 != prev_meta.5;
        let rang = frame.bell;
        let started = Instant::now();
        let plain = strip_sgr(&frame.lines.join("\n"));
        let state = self.classify(changed, prev_change, &plain).await;
        crate::perf::PERF.classify.add(started.elapsed());

        if !changed && state == prev_state && !rang {
            return;
        }

        let view = ScreenView::from_frame(name, seq + 1, cols, rows, 0, frame, 0);

        let mut dirty_list = false;
        {
            let mut live = self.live.write().await;
            let Some(l) = live.get_mut(name) else { return };
            l.hash = hash;
            l.seq += 1;
            if changed {
                l.last_change = now_ms();
            }
            if l.set_state(state) {
                dirty_list = true;
            }
            if title_moved {
                dirty_list = true;
            }
            if rang && !l.bell {
                l.bell = true;
                dirty_list = true;
            }
            l.screen = Some(view.clone());
            l.plain = plain;
        }

        if changed {
            crate::perf::PERF.frames.fetch_add(1, Ordering::Relaxed);
            let _ = self.events.send(Event::Screen { screen: view });
        }
        if dirty_list {
            let _ = self.events.send(Event::Sessions {
                sessions: self.views().await,
            });
        }
    }

    async fn mark_down(self: &Arc<Self>, name: &str) {
        if self.is_ephemeral(name).await {
            self.forget(name).await;
            return;
        }

        let mut changed = false;
        {
            let mut live = self.live.write().await;
            if let Some(l) = live.get_mut(name) {
                if l.set_state(State::Down) {
                    changed = true;
                }
                l.bell = false;
                l.screen = None;
                l.emu = None;
                // The generated title belongs to the process that just exited;
                // the downed session should fall back to its ordinary label.
                // Resetting the generation also rejects a late worker result.
                l.title = TitleCapture::default();
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

    pub async fn scroll_capture(
        &self,
        name: &str,
        off: u32,
        request_id: u64,
    ) -> Option<ScreenView> {
        if off == 0 {
            return self.screen(name).await;
        }

        let (emu, seq, cols, rows) = {
            let live = self.live.read().await;
            let l = live.get(name)?;
            (l.emu.clone()?, l.seq, l.cols, l.rows)
        };

        if let Ok(c) = self.scroll_cache.lock() {
            if let Some(cached) = c.get(name) {
                if cached.live_seq == seq
                    && cached.off == off
                    && cached.cols == cols
                    && cached.rows == rows
                {
                    let mut view = cached.view.clone();
                    view.request_id = request_id;
                    return Some(view);
                }
            }
        }

        let (grid, achieved, title) = {
            let mut e = emu.lock().ok()?;
            e.scroll_snapshot(off)
        };
        if achieved == 0 {
            return self.screen(name).await;
        }
        let frame = crate::emu::SessionEmu::frame_from_grid(grid, cols, rows, title);
        let view = ScreenView::from_frame(name, seq, cols, rows, achieved, frame, request_id);

        if let Ok(mut c) = self.scroll_cache.lock() {
            c.insert(
                name.to_string(),
                CachedScroll {
                    live_seq: seq,
                    off,
                    cols,
                    rows,
                    view: view.clone(),
                },
            );
        }
        Some(view)
    }
}

fn validate_config(cfg: &Config) -> Result<()> {
    cfg.daemon
        .bind
        .parse::<std::net::SocketAddr>()
        .with_context(|| format!("bad bind address {:?}", cfg.daemon.bind))?;
    if cfg.daemon.tmux_socket.trim().is_empty() {
        bail!("tmux socket name must not be empty");
    }

    let table = crate::presets::table();
    for (field, name) in [
        ("agent", &cfg.defaults.agent),
        ("shell", &cfg.defaults.shell),
    ] {
        let name = name.trim();
        if name.is_empty() {
            bail!("the default {field} must name a command preset");
        }
        if table.command(name).is_none() {
            bail!("unknown command preset: {name}");
        }
    }
    for s in &cfg.sessions {
        let Some(p) = cfg.project(&s.project) else {
            continue;
        };
        cfg.network_of(s, p)?;
    }
    Ok(())
}

fn json_to_toml(value: Value) -> Result<toml::Value> {
    Ok(match value {
        Value::Null => bail!("null is not a valid config patch value"),
        Value::Bool(v) => toml::Value::Boolean(v),
        Value::Number(v) => {
            if let Some(v) = v.as_i64() {
                toml::Value::Integer(v)
            } else if let Some(v) = v.as_u64().and_then(|v| i64::try_from(v).ok()) {
                toml::Value::Integer(v)
            } else if let Some(v) = v.as_f64() {
                toml::Value::Float(v)
            } else {
                bail!("invalid JSON number in config patch")
            }
        }
        Value::String(v) => toml::Value::String(v),
        Value::Array(v) => toml::Value::Array(
            v.into_iter()
                .map(json_to_toml)
                .collect::<Result<Vec<_>>>()?,
        ),
        Value::Object(v) => toml::Value::Table(
            v.into_iter()
                .map(|(key, value)| Ok((key, json_to_toml(value)?)))
                .collect::<Result<toml::map::Map<_, _>>>()?,
        ),
    })
}

fn merge_toml(base: &mut toml::Value, patch: toml::Value) {
    match patch {
        toml::Value::Table(patch) => {
            let Some(base) = base.as_table_mut() else {
                *base = toml::Value::Table(patch);
                return;
            };
            for (key, value) in patch {
                if let Some(existing) = base.get_mut(&key) {
                    merge_toml(existing, value);
                } else {
                    base.insert(key, value);
                }
            }
        }
        patch => *base = patch,
    }
}

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
    use std::path::{Path, PathBuf};

    use super::{
        breadcrumb_block, check_breadcrumbs, check_name, check_shortcut, compile_rules, free_name,
        free_project_name, json_to_toml, match_rules, merge_input, merge_toml,
        normalize_action_command, normalize_path, render_template, render_template_with, settle,
        slug, strip_sgr, title_agent, title_settings, Composer, Input, Live, State, Submission,
        TemplateVars, TitleAgent, TitleCapture, BOOT_COLS, BOOT_ROWS, INPUT_BATCH,
    };
    use crate::config::{Config, ProjectCfg, SessionCfg, ShortcutCfg, ShortcutKind};

    #[test]
    fn file_action_paths_normalize_and_replace_the_raw_quoted_path() {
        let path = normalize_path(Path::new("/tmp/slop/../repo/file name"));
        assert_eq!(path, PathBuf::from("/tmp/repo/file name"));
        assert_eq!(
            normalize_action_command("~/repo/file name", &path, "du -sh '~/repo/file name'"),
            "du -sh '/tmp/repo/file name'"
        );
    }

    #[test]
    fn codex_composer_recovers_the_submitted_prompt() {
        let mut c = Composer::ready();
        c.literal("fix teh parser");
        for _ in 0..8 {
            c.key("Left");
        }
        c.key("BSpace");
        c.literal("he");
        c.key("DC");
        let Some(Submission::Prompt(prompt)) = c.key("Enter") else {
            panic!("expected a prompt")
        };
        assert_eq!(prompt, "fix the parser");
    }

    #[test]
    fn codex_composer_rearms_new_and_skips_uncertain_input() {
        let mut c = Composer::ready();
        c.literal("/new parser work");
        let Some(Submission::New(name)) = c.key("Enter") else {
            panic!("expected a boundary")
        };
        assert_eq!(name.as_deref(), Some("parser work"));

        c.literal("history entry");
        c.key("Up");
        assert!(c.key("Enter").is_none());
        c.literal("fresh prompt");
        assert!(matches!(c.key("Enter"), Some(Submission::Prompt(_))));
    }

    #[test]
    fn waiting_dialog_answers_are_not_prompt_titles() {
        assert!(super::is_dialog_answer(" yes "));
        assert!(super::is_dialog_answer("1"));
        assert!(!super::is_dialog_answer("fix the parser"));
    }

    #[test]
    fn title_agents_cover_presets_and_explicit_commands() {
        let mut cfg = Config::default();
        cfg.daemon.pi_title_model = "pi-title".into();

        let codex = SessionCfg {
            cmd: Some("codex --yolo".into()),
            ..Default::default()
        };
        assert!(matches!(title_agent(&cfg, &codex), Some(TitleAgent::Codex)));

        let pi = SessionCfg {
            cmd: Some("pi --model test".into()),
            ..Default::default()
        };
        assert!(matches!(title_agent(&cfg, &pi), Some(TitleAgent::Pi)));
        assert_eq!(title_settings(&cfg, TitleAgent::Pi).1, "pi-title");

        let other = SessionCfg {
            cmd: Some("opencode".into()),
            ..Default::default()
        };
        assert!(title_agent(&cfg, &other).is_none());
    }

    #[test]
    fn config_patches_merge_nested_fields_without_resetting_unmentioned_values() {
        let mut document: toml::Value = toml::from_str(
            r#"
[daemon]
bind = "127.0.0.1:7717"
token = "secret"
tmux_socket = "slopworld"
poll_ms = 80
future = "keep"

[defaults]
agent = "claude"
shell = "shell"
"#,
        )
        .expect("config parses");

        let patch = json_to_toml(serde_json::json!({
            "daemon": { "poll_ms": 120 },
        }))
        .expect("patch converts");
        merge_toml(&mut document, patch);

        assert_eq!(document["daemon"]["poll_ms"].as_integer(), Some(120));
        assert_eq!(document["daemon"]["future"].as_str(), Some("keep"));
        assert_eq!(document["defaults"]["agent"].as_str(), Some("claude"));
        assert_eq!(document["daemon"]["token"].as_str(), Some("secret"));
    }

    fn seeded_rules() -> Vec<(State, regex::Regex)> {
        let cfg = Config::parse(
            r#"
[[state_rule]]
state = "waiting"
pattern = '(?i)(do you want|❯\s*1\.|yes, and don.t ask again|press enter to continue)'

[[state_rule]]
state = "working"
pattern = '(?i)(esc to interrupt|to interrupt\))'
"#,
        )
        .expect("config parses");
        compile_rules(&cfg)
    }

    #[test]
    fn work_started_beats_the_question_that_started_it() {
        let screen = "\
> fix the parser

  Do you want to make this edit to lexer.rs?
  ❯ 1. Yes
    2. No

  Updated lexer.rs with 3 additions

* Thinking… (12s · esc to interrupt)
";
        assert_eq!(match_rules(&seeded_rules(), screen), Some(State::Working));
    }

    #[test]
    fn a_question_with_nothing_under_it_is_waiting() {
        let screen = "\
  Updated lexer.rs with 3 additions

  Do you want to make this edit to parser.rs?
  ❯ 1. Yes
    2. No
";
        assert_eq!(match_rules(&seeded_rules(), screen), Some(State::Waiting));
    }

    #[test]
    fn trailing_blanks_do_not_spend_the_tail() {
        let mut screen = String::from("* Working… (esc to interrupt)\n");
        screen.push_str(&"\n".repeat(30));
        assert_eq!(match_rules(&seeded_rules(), &screen), Some(State::Working));
    }

    #[test]
    fn a_rule_out_of_reach_of_the_tail_says_nothing() {
        let mut screen = String::from("  Do you want to make this edit?\n");
        for i in 0..20 {
            screen.push_str(&format!("  line {i}\n"));
        }
        screen.push_str("> \n");
        assert_eq!(match_rules(&seeded_rules(), &screen), None);
    }

    #[test]
    fn merges_a_run_of_mouse_reports() {
        let batch = merge_input(vec![
            Input::Bytes(b"\x1b[<64;1;1M".to_vec()),
            Input::Bytes(b"\x1b[<64;1;1M".to_vec()),
            Input::Bytes(b"\x1b[<64;1;1M".to_vec()),
        ]);
        assert_eq!(batch.len(), 1);
        match &batch[0] {
            Input::Bytes(b) => assert_eq!(b.len(), 30),
            _ => panic!("wrong kind"),
        }
    }

    #[test]
    fn stops_merging_at_the_command_ceiling() {
        let items: Vec<Input> = (0..600).map(|_| Input::Bytes(vec![b'x'; 2])).collect();
        let batch = merge_input(items);
        assert!(batch.len() > 1, "1200 bytes must not become one command");
        for item in &batch {
            match item {
                Input::Bytes(b) => assert!(b.len() <= INPUT_BATCH),
                _ => panic!("wrong kind"),
            }
        }
    }

    #[test]
    fn a_paste_is_never_merged_and_never_reordered() {
        let batch = merge_input(vec![
            Input::Bytes(b"ab".to_vec()),
            Input::Paste(b"hello".to_vec()),
            Input::Bytes(b"cd".to_vec()),
            Input::Keys {
                keys: vec!["Enter".into()],
                literal: false,
            },
        ]);
        assert_eq!(batch.len(), 4);
        assert!(matches!(&batch[1], Input::Paste(p) if p == b"hello"));
        assert!(matches!(&batch[3], Input::Keys { .. }));
    }

    #[test]
    fn literal_and_named_keys_do_not_share_a_command() {
        let batch = merge_input(vec![
            Input::Keys {
                keys: vec!["Up".into()],
                literal: false,
            },
            Input::Keys {
                keys: vec!["Down".into()],
                literal: false,
            },
            Input::Keys {
                keys: vec!["hi".into()],
                literal: true,
            },
        ]);
        assert_eq!(batch.len(), 2);
        match &batch[0] {
            Input::Keys { keys, literal } => {
                assert_eq!(keys, &["Up".to_string(), "Down".to_string()]);
                assert!(!literal);
            }
            _ => panic!("wrong kind"),
        }
    }

    fn placeholder() -> Live {
        Live {
            cfg: SessionCfg::default(),
            ephemeral: true,
            host: false,
            state: State::Down,
            seq: 0,
            retick_seq: 0,
            hash: 0,
            last_change: 0,
            state_since: 0,
            bell: false,
            cols: BOOT_COLS,
            rows: BOOT_ROWS,
            plain: String::new(),
            screen: None,
            emu: None,
            reader: None,
            input: None,
            breadcrumbs: Vec::new(),
            breadcrumbs_pending: false,
            title: TitleCapture::default(),
        }
    }

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

    #[test]
    fn temp_projects_settle_on_a_path_under_the_root() {
        let mut p = ProjectCfg {
            name: "scratch pad".into(),
            temp: true,
            ..Default::default()
        };
        settle(&mut p);
        assert_eq!(p.dir, "/tmp/slopworld/scratch-pad");

        p.dir = "/home/you/git/repo".into();
        settle(&mut p);
        assert_eq!(p.dir, "/tmp/slopworld/scratch-pad");

        let mut plain = ProjectCfg {
            name: "repo".into(),
            dir: "/home/you/git/repo".into(),
            ..Default::default()
        };
        settle(&mut plain);
        assert_eq!(plain.dir, "/home/you/git/repo");
    }

    #[test]
    fn an_entry_must_have_something_to_send() {
        let mut cfg = Config::default();
        cfg.projects.push(ProjectCfg {
            name: "repo".into(),
            dir: "/home/you/git/repo".into(),
            ..Default::default()
        });

        let errand = ShortcutCfg {
            name: "view-main-rs".into(),
            kind: ShortcutKind::Shell,
            project: "repo".into(),
            command: Some("less -R -- /home/you/git/repo/main.rs".into()),
            text: String::new(),
            ..Default::default()
        };
        assert!(check_shortcut(&cfg, &errand).is_err());

        let nowhere = ShortcutCfg {
            project: String::new(),
            text: "hello".into(),
            ..errand.clone()
        };
        assert!(check_shortcut(&cfg, &nowhere).is_err());

        let gone = ShortcutCfg {
            project: "not-a-project".into(),
            text: "hello".into(),
            ..errand.clone()
        };
        assert!(check_shortcut(&cfg, &gone).is_err());

        let fine = ShortcutCfg {
            text: "hello".into(),
            ..errand
        };
        assert!(check_shortcut(&cfg, &fine).is_ok());
    }

    #[test]
    fn every_tip_mention_is_its_own_draw() {
        let tips: Vec<String> = ["one", "two", "three", "four", "five"]
            .iter()
            .map(|s| s.to_string())
            .collect();
        let out = render_template(
            "- {{ random_tip }}\n- {{random_tip}}\n- {{ random_tip}}\n\
             - {{ random_tip }}\n- {{ random_tip }}",
            &tips,
        );
        for tip in &tips {
            assert_eq!(out.matches(tip.as_str()).count(), 1, "{tip} exactly once");
        }

        let short = render_template(
            "{{ random_tip }}/{{ random_tip }}/{{ random_tip }}",
            &tips[..2],
        );
        assert_eq!(short, "one/two/one");
    }

    #[test]
    fn a_template_with_nothing_to_fill_it_is_left_alone() {
        let text = "- {{ random_tip }} and {{ whatever }}";
        assert_eq!(render_template(text, &[]), text);

        let tips = vec!["a tip".to_string()];
        assert_eq!(render_template(text, &tips), "- a tip and {{ whatever }}");

        assert_eq!(
            render_template("keep {{ random_tip", &tips),
            "keep {{ random_tip"
        );
    }

    #[test]
    fn breadcrumb_context_variables_render_and_unknown_variables_survive() {
        let vars = TemplateVars {
            agent: "Ada",
            project: "slopworld",
            directory: "/src/slopworld",
            command: "codex",
        };
        assert_eq!(
            render_template_with(
                "{{ agent }} in {{ project }} at {{ directory }} via {{ command }}; {{ later }}",
                &[],
                Some(&vars),
            ),
            "Ada in slopworld at /src/slopworld via codex; {{ later }}"
        );
    }

    #[test]
    fn one_line_breadcrumbs_share_a_list_and_a_block_keeps_its_shape() {
        let block = breadcrumb_block(&[
            "never commit".to_string(),
            "always lint".to_string(),
            "___\n\nUseful tips:\n\n- a\n- b".to_string(),
            "and one more".to_string(),
        ]);
        assert_eq!(
            block,
            "\n\n- never commit\n- always lint\n\n___\n\nUseful tips:\n\n- a\n- b\n\n- and one more"
        );

        assert_eq!(breadcrumb_block(&[]), "");
        assert_eq!(breadcrumb_block(&["   ".to_string()]), "");
    }

    #[test]
    fn an_attachment_must_name_a_breadcrumb() {
        let mut cfg = Config::default();
        cfg.shortcuts.push(ShortcutCfg {
            name: "tests".into(),
            kind: ShortcutKind::Shell,
            text: "make test".into(),
            ..Default::default()
        });

        assert!(check_breadcrumbs(&cfg, &["Useful tips".to_string()]).is_ok());
        assert!(check_breadcrumbs(&cfg, &["tests".to_string()]).is_err());
        assert!(check_breadcrumbs(&cfg, &["gone".to_string()]).is_err());
    }

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
    fn strips_color_and_keeps_text() {
        assert_eq!(strip_sgr("\x1b[31mred\x1b[0m done"), "red done");
        assert_eq!(strip_sgr("esc to interrupt"), "esc to interrupt");
        assert_eq!(strip_sgr("\x1b]0;title\x07body"), "body");
        assert_eq!(strip_sgr("\x1b[38;5;214m❯ 1.\x1b[m"), "❯ 1.");
    }
}
