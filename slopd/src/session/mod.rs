use std::collections::HashMap;
use std::hash::{Hash, Hasher};
use std::path::{Component, Path, PathBuf};
use std::sync::atomic::{AtomicU64, AtomicUsize, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use anyhow::{bail, Context, Result};
use regex::Regex;
use serde::{Deserialize, Serialize};
use serde_json::Value;
use tokio::io::{AsyncRead, AsyncReadExt};
use tokio::sync::{broadcast, mpsc, RwLock};
use tokio::task::JoinHandle;

mod ctrl;
mod view;

pub(super) use ctrl::CachedScroll;
pub use ctrl::{ClientGuard, Manager, WatchGuard};
pub use view::{ScreenView, SessionView};

use crate::config::{
    expand, Config, ProjectCfg, SessionCfg, ShortcutCfg, ShortcutKind, ShortcutLink, TitlePolicy,
};
use crate::emu::{Frame, SessionEmu};
use crate::tmux::Tmux;

const IDLE_MS: u64 = 10_000;

// Limit stale prompts near the top of a screen from overriding newer status below them.
const TAIL_LINES: usize = 12;

// Unwatched panes still need classification, but not reader-rate rendering.
const UNWATCHED_MS: u64 = 200;

const FILE_ACTION_TIMEOUT: Duration = Duration::from_secs(15);
const FILE_ACTION_STREAM_LIMIT: usize = 4096;
const MAX_MANUAL_LABEL_CHARS: usize = 60;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum State {
    Down,
    Working,
    Waiting,
    Idle,
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
#[serde(tag = "t", rename_all = "lowercase")]
pub enum Event {
    Sessions { sessions: Vec<SessionView> },
    Projects { projects: Vec<ProjectCfg> },
    Shortcuts { shortcuts: Vec<ShortcutCfg> },
    Screen { screen: ScreenView },
    Usage { usage: crate::usage::Snapshot },
    Audio { audio: crate::audio::AudioState },
    Jukebox { jukebox: crate::jukebox::Catalog },
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
    // Never persisted in config: host errands carry this through a private tmux option so the
    // daemon can recover it when tmux outlives a daemon restart.
    host: bool,
    // Host shells keep their last tmux cwd separately from the project's configured root.
    host_path: String,
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
    // Once mode counts the first request, not only a successful response. This keeps a
    // transient OpenRouter failure from turning every later prompt into another billable try.
    once_requested: bool,
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

fn title_settings(cfg: &Config, session: &SessionCfg, host: bool) -> Option<(TitlePolicy, String)> {
    if session
        .label
        .as_deref()
        .is_some_and(|label| !label.trim().is_empty())
    {
        return None;
    }

    if host {
        return cfg
            .daemon
            .host_titles
            .then(|| (TitlePolicy::Always, cfg.daemon.title_model.clone()));
    }

    let agent = title_agent(cfg, session)?;
    Some(match agent {
        TitleAgent::Codex => (cfg.daemon.agent_titles, cfg.daemon.title_model.clone()),
        TitleAgent::Pi => (cfg.daemon.pi_titles, cfg.daemon.title_model.clone()),
    })
}

impl Default for TitleCapture {
    fn default() -> Self {
        Self {
            composer: Composer::ready(),
            conversation: 0,
            generation: 0,
            pending: false,
            once_requested: false,
            override_title: None,
        }
    }
}

impl TitleCapture {
    fn once_available(&self) -> bool {
        !self.once_requested
    }

    fn consume_once(&mut self) {
        self.once_requested = true;
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
            // Codex's line editor accepts these readline-style aliases as well as the named
            // cursor keys emitted by the terminal window.
            "C-j" | "C-m" => return self.submit(),
            "BSpace" if self.cursor > 0 => {
                self.cursor -= 1;
                self.text.remove(self.cursor);
            }
            "DC" if self.cursor < self.text.len() => {
                self.text.remove(self.cursor);
            }
            "Left" if self.cursor > 0 => self.cursor -= 1,
            "Right" if self.cursor < self.text.len() => self.cursor += 1,
            "C-b" if self.cursor > 0 => self.cursor -= 1,
            "C-f" if self.cursor < self.text.len() => self.cursor += 1,
            "C-d" if self.cursor < self.text.len() => {
                self.text.remove(self.cursor);
            }
            "C-h" if self.cursor > 0 => {
                self.cursor -= 1;
                self.text.remove(self.cursor);
            }
            "C-Left" | "M-Left" => self.word_left(),
            "C-Right" | "M-Right" => self.word_right(),
            "C-DC" | "M-DC" => self.delete_word_right(),
            "M-b" => self.word_left(),
            "M-f" => self.word_right(),
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
            // longer proves what Codex will receive. Clear the stale mirror as well: if Escape
            // cancelled the editor, the next prompt must not inherit the cancelled text.
            _ => self.invalidate(),
        }
        None
    }

    fn word_left(&mut self) {
        while self.cursor > 0 && self.text[self.cursor - 1].is_whitespace() {
            self.cursor -= 1;
        }
        while self.cursor > 0 && !self.text[self.cursor - 1].is_whitespace() {
            self.cursor -= 1;
        }
    }

    fn word_right(&mut self) {
        while self.cursor < self.text.len() && self.text[self.cursor].is_whitespace() {
            self.cursor += 1;
        }
        while self.cursor < self.text.len() && !self.text[self.cursor].is_whitespace() {
            self.cursor += 1;
        }
    }

    fn delete_word_right(&mut self) {
        let start = self.cursor;
        self.word_right();
        self.text.drain(start..self.cursor);
        self.cursor = start;
    }

    fn invalidate(&mut self) {
        self.text.clear();
        self.cursor = 0;
        self.certain = false;
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

fn begin_title_request(
    live: &mut Live,
    prompt: String,
    key_file: String,
    model: String,
) -> TitleRequest {
    live.title.generation = live.title.generation.wrapping_add(1);
    live.title.pending = true;
    TitleRequest {
        prompt,
        conversation: live.title.conversation,
        generation: live.title.generation,
        key_file,
        model,
    }
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

const CFG_CHECK_MS: u64 = 2_000;

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

pub(super) fn check_name(name: &str) -> Result<()> {
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
    if let Some(dns) = &p.dns {
        dns.validate(&format!("project {}", p.name))?;
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
    if let Some(dns) = &s.dns {
        dns.validate(&format!("agent {}", s.name))?;
    }
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

pub(crate) fn hold_action_command(command: &str) -> String {
    format!(
        "bash -lc {}",
        shell_quote(&format!("{command}; exec \"${{SHELL:-bash}}\""))
    )
}

fn normalize_action_command(raw_path: &str, path: &Path, command: &str) -> String {
    let raw = shell_quote(raw_path);
    let absolute = shell_quote(&path.to_string_lossy());
    // The mod expands these before sending the request, but keep the daemon authoritative so
    // direct API callers and older mod DLLs cannot hand the executor a literal template.
    command
        .replace("{{ absolute_path }}", &absolute)
        .replace("{{absolute_path}}", &absolute)
        .replace(&raw, &absolute)
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

#[path = "../manager/mod.rs"]
mod manager;

fn validate_config(cfg: &Config) -> Result<()> {
    cfg.daemon
        .bind
        .parse::<std::net::SocketAddr>()
        .with_context(|| format!("bad bind address {:?}", cfg.daemon.bind))?;
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
        if let Some(dns) = &s.dns {
            dns.validate(&format!("agent {}", s.name))?;
        }
        let Some(p) = cfg.project(&s.project) else {
            continue;
        };
        cfg.network_of(s, p)?;
    }
    for p in &cfg.projects {
        if let Some(dns) = &p.dns {
            dns.validate(&format!("project {}", p.name))?;
        }
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
        free_project_name, hold_action_command, json_to_toml, match_rules, merge_input, merge_toml,
        normalize_action_command, normalize_path, render_template, render_template_with, settle,
        slug, strip_sgr, title_agent, title_settings, Composer, Input, Live, State, Submission,
        TemplateVars, TitleAgent, TitleCapture, BOOT_COLS, BOOT_ROWS, INPUT_BATCH,
    };
    use crate::config::{Config, ProjectCfg, SessionCfg, ShortcutCfg, ShortcutKind, TitlePolicy};

    #[test]
    fn file_action_paths_normalize_and_replace_the_raw_quoted_path() {
        let path = normalize_path(Path::new("/tmp/slop/../repo/file name"));
        assert_eq!(path, PathBuf::from("/tmp/repo/file name"));
        assert_eq!(
            normalize_action_command("~/repo/file name", &path, "du -sh '~/repo/file name'"),
            "du -sh '/tmp/repo/file name'"
        );
        assert_eq!(
            normalize_action_command("~/repo/file name", &path, "du -sh {{ absolute_path }}"),
            "du -sh '/tmp/repo/file name'"
        );
        assert_eq!(
            crate::sandbox::shell_split(&hold_action_command("du -sh '/tmp/repo/file name'")),
            vec![
                "bash",
                "-lc",
                "du -sh '/tmp/repo/file name'; exec \"${SHELL:-bash}\""
            ]
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
    fn codex_composer_handles_common_controls_and_resynchronizes() {
        let mut c = Composer::ready();
        c.literal("fix parser");
        c.key("Home");
        c.key("C-f");
        c.key("C-d");
        c.literal("i");
        let Some(Submission::Prompt(prompt)) = c.key("C-j") else {
            panic!("expected Ctrl-J to submit")
        };
        assert_eq!(prompt, "fix parser");

        c.literal("stale input");
        c.key("Escape");
        c.literal("replacement");
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
        cfg.daemon.title_model = "shared-title".into();

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
        assert_eq!(title_settings(&cfg, &pi, false).unwrap().1, "shared-title");

        let labeled = SessionCfg {
            label: Some("keep this name".into()),
            cmd: Some("pi --model test".into()),
            ..Default::default()
        };
        assert!(title_settings(&cfg, &labeled, false).is_none());

        let host = SessionCfg {
            cmd: Some("bash".into()),
            ..Default::default()
        };
        assert_eq!(
            title_settings(&cfg, &host, true).unwrap().0,
            TitlePolicy::Always
        );
        cfg.daemon.host_titles = false;
        assert!(title_settings(&cfg, &host, true).is_none());

        let other = SessionCfg {
            cmd: Some("opencode".into()),
            ..Default::default()
        };
        assert!(title_agent(&cfg, &other).is_none());
    }

    #[test]
    fn once_title_is_consumed_before_the_worker_finishes() {
        let mut capture = TitleCapture::default();
        assert!(capture.once_available());
        capture.consume_once();
        assert!(!capture.once_available());
        assert!(capture.override_title.is_none());
    }

    #[test]
    fn config_patches_merge_nested_fields_without_resetting_unmentioned_values() {
        let mut document: toml::Value = toml::from_str(
            r#"
[daemon]
bind = "127.0.0.1:7717"
token = "secret"
future = "keep"

[defaults]
agent = "claude"
shell = "bash"
"#,
        )
        .expect("config parses");

        let patch = json_to_toml(serde_json::json!({
            "daemon": { "usage_poll_secs": 120 },
        }))
        .expect("patch converts");
        merge_toml(&mut document, patch);

        assert_eq!(
            document["daemon"]["usage_poll_secs"].as_integer(),
            Some(120)
        );
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
            host_path: String::new(),
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
