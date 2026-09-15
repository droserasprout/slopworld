use std::collections::HashMap;
use std::path::{Component, Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex, OnceLock};
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use anyhow::{bail, Context, Result};
use regex::Regex;
use serde::Deserialize;
use serde_json::Value;
use tokio::sync::{broadcast, mpsc, RwLock};
use tokio::task::JoinHandle;

mod agent_templates;
mod ctrl;
mod input;
mod template;
mod text;
mod title;
mod validation;
mod view;

#[path = "../manager/mod.rs"]
mod manager;

pub(crate) use agent_templates::{AgentTemplate, AgentTemplateStore};
#[cfg(test)]
pub(crate) use ctrl::test_manager;
pub(super) use ctrl::CachedScroll;
pub use ctrl::{ClientGuard, Manager, WatchGuard};
pub(crate) use view::FrameViewArgs;
pub use view::{ScreenView, SessionView};

use input::{merge_input, Input};
use template::{render_template, render_template_with, TemplateVars};
pub use text::strip_sgr;
pub(crate) use text::strip_sgr_tail;
use title::{
    begin_title_request, is_dialog_answer, prompt_is_long_enough, title_settings, Composer,
    Submission, TitleCapture, TitleRequest,
};
use validation::{
    absolute_path, breadcrumb_block, check_belongs, check_breadcrumbs, check_library_item,
    check_name, check_project, free_name, free_project_name, json_to_toml, merge_toml,
    normalize_action_command, project_action_path, settle, slug,
};
pub(crate) use validation::{hold_action_command, validate_config};

#[cfg(test)]
use input::INPUT_BATCH;
#[cfg(test)]
use title::{title_agent, TitleAgent};
#[cfg(test)]
use validation::{check_mounts, normalize_path};

use crate::config::{
    expand, Config, LibraryItemCfg, LibraryItemKind, LibraryItemLink, NetworkMode, ProjectCfg,
    SessionCfg, TitlePolicy,
};
use crate::emu::{Frame, SessionEmu};
use crate::tmux::Tmux;

const IDLE_MS: u64 = 10_000;

// Limit stale prompts near the top of a screen from overriding newer status below them.
const TAIL_LINES: usize = 12;

// Unwatched panes still need classification, but not reader-rate rendering.
const UNWATCHED_MS: u64 = 200;

// Host cwd/process metadata is display state, not frame classification. Keep it fresh with one
// combined tmux query per host rather than two subprocess waves on every state tick.
const HOST_METADATA_POLL_MS: u64 = 2_000;

const FILE_ACTION_TIMEOUT: Duration = Duration::from_secs(15);
const FILE_ACTION_STREAM_LIMIT: usize = 4096;
const MAX_MANUAL_LABEL_CHARS: usize = 60;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum State {
    Down,
    Working,
    Waiting,
    Idle,
}

crate::wire_enum!(State, {
    State::Down => crate::shared::protocol::enums::agent_state::DOWN,
    State::Working => crate::shared::protocol::enums::agent_state::WORKING,
    State::Waiting => crate::shared::protocol::enums::agent_state::WAITING,
    State::Idle => crate::shared::protocol::enums::agent_state::IDLE,
});

#[derive(Debug, Clone, Default, Deserialize)]
pub struct RunWhere {
    pub cols: Option<u16>,
    pub rows: Option<u16>,
    #[serde(default)]
    pub project: Option<String>,
    #[serde(default)]
    pub temp: bool,
    #[serde(default)]
    pub random_tips: Vec<String>,
}

#[derive(Debug, Clone)]
pub enum Event {
    Capabilities {
        capabilities: crate::runtime::Capabilities,
    },
    Sessions {
        sessions: Vec<SessionView>,
    },
    Projects {
        projects: Vec<ProjectCfg>,
    },
    Library {
        library: Vec<LibraryItemCfg>,
    },
    Screen {
        screen: ScreenView,
    },
    Usage {
        usage: crate::usage::Snapshot,
    },
    Audio {
        audio: crate::audio::AudioState,
    },
    Jukebox {
        jukebox: crate::jukebox::Catalog,
    },
}

crate::wire_event_serialize!(Event, {
    Capabilities { capabilities },
    Sessions { sessions },
    Projects { projects },
    Library { library },
    Screen { screen },
    Usage { usage },
    Audio { audio },
    Jukebox { jukebox },
});

/// One immutable event shared by all WebSocket pumps. The event itself remains separate from
/// its cached wire representation because scoped session lists may need a filtered envelope.
pub(crate) struct EventMessage {
    event: Event,
    encoded: OnceLock<Arc<str>>,
}

impl EventMessage {
    pub(crate) fn new(event: Event) -> Arc<Self> {
        Arc::new(Self {
            event,
            encoded: OnceLock::new(),
        })
    }

    pub(crate) fn event(&self) -> &Event {
        &self.event
    }

    pub(crate) fn encoded(&self) -> Arc<str> {
        self.encoded
            .get_or_init(|| {
                let _perf = crate::perf::timer("websocket-serialize");
                Arc::from(serde_json::to_string(&self.event).unwrap_or_default())
            })
            .clone()
    }
}

#[derive(Debug, Clone)]
pub(crate) enum AuthChange {
    GrantsRevoked,
    RootTokenChanged,
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
    // Host-only: whether tmux currently has a foreground command other than the login shell.
    process_running: bool,
    seq: u64,
    // Last sequence classified by retick; equal means only idle decay can change state.
    retick_seq: u64,
    hash: u64,
    activity_hash: u64,
    last_change: u64,
    // State time is independent of pane redraws, which can continue several times a second.
    state_since: u64,
    // Sticky until a client subscribes; the frame containing the bell is transient.
    bell: bool,
    cols: u16,
    rows: u16,
    plain: Arc<String>,
    // Regex matching is keyed by the stripped visible text and the accepted rules revision.
    // Activity decay is sampled separately, so a quiet pane can age without rescanning its
    // unchanged tail.
    rule_cache: Option<RuleCache>,
    screen: Option<ScreenView>,
    emu: Option<Arc<Mutex<SessionEmu>>>,
    reader: Option<JoinHandle<()>>,
    // Identifies the reader that owns the current emulator. A stale reader may finish while a
    // replacement is starting; it must not tear down the replacement's state.
    reader_token: Option<Arc<()>>,
    input: Option<mpsc::UnboundedSender<Input>>,
    // Spliced immediately before the first Enter after process start.
    breadcrumbs: Vec<u8>,
    breadcrumbs_pending: bool,
    // Set while the startup auto-resume sequence is waiting or queued. The client uses this
    // to keep user keystrokes behind the sequence in the input queue.
    auto_resume_pending: bool,
    // Distinguishes successive processes under the same durable session name. Startup input
    // captured for an old process must not land in a quick stop/start replacement.
    run_id: u64,
    title: TitleCapture,
}

#[derive(Clone)]
struct RuleCache {
    revision: u64,
    text: Arc<String>,
    matched: Option<State>,
}

struct Classification {
    state: State,
    rules_revision: u64,
    matched: Option<State>,
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

const CFG_CHECK_MS: u64 = 2_000;
const PRESETS_CHECK_MS: u64 = 2_000;
const JUKEBOX_CHECK_MS: u64 = 2_000;

const BOOT_COLS: u16 = 120;
const BOOT_ROWS: u16 = 34;

const READY_MS: u64 = 30_000;
const SETTLE_MS: u64 = 750;
const ENTER_GAP_MS: u64 = 150;
// A bracketed paste changes the agent TUI's input state asynchronously. Give a cold or
// backgrounded pane time to commit that state before the separate Enter reaches it.
pub(crate) const DELIVERY_ENTER_GAP_MS: u64 = 1_000;

async fn disk_mtime(path: &std::path::Path) -> Option<SystemTime> {
    tokio::fs::metadata(path).await.ok()?.modified().ok()
}

fn now_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0)
}

fn match_rules(rules: &[(State, Regex)], text: &str) -> Option<State> {
    // The lowest matching line wins; config order only breaks ties on that line. Skip only
    // trailing blanks when finding the screen's end: blanks within the tail still consume one
    // of its TAIL_LINES, just as the screen's physical rows do.
    let mut lines = text.lines().rev();
    let mut line = lines.find(|line| !line.trim().is_empty())?;
    for index in 0..TAIL_LINES {
        for (state, re) in rules {
            if re.is_match(line) {
                return Some(*state);
            }
        }
        if index + 1 == TAIL_LINES {
            break;
        }
        let Some(next) = lines.next() else {
            break;
        };
        line = next;
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

#[cfg(test)]
mod tests {
    use std::collections::HashMap;
    use std::path::{Path, PathBuf};
    use std::sync::Arc;

    use super::{
        breadcrumb_block, check_breadcrumbs, check_library_item, check_name, check_project,
        compile_rules, free_name, free_project_name, hold_action_command, json_to_toml,
        match_rules, merge_input, merge_toml, normalize_action_command, normalize_path,
        project_action_path, prompt_is_long_enough, render_template, render_template_with, settle,
        slug, strip_sgr, title_agent, title_settings, Composer, Event, EventMessage, Input, Live,
        ScreenView, State, Submission, TemplateVars, TitleAgent, TitleCapture, BOOT_COLS,
        BOOT_ROWS, INPUT_BATCH, TAIL_LINES,
    };
    use crate::config::{Config, LibraryItemCfg, LibraryItemKind, ProjectCfg, SessionCfg};

    #[test]
    fn event_message_reuses_its_encoded_json() {
        let event = EventMessage::new(Event::Usage {
            usage: Default::default(),
        });
        let first = event.encoded();
        let second = event.encoded();

        assert!(Arc::ptr_eq(&first, &second));
        assert!(first.starts_with("{\"t\":\"usage\""));
    }

    #[test]
    fn every_event_uses_its_contract_tag_and_payload_name() {
        let screen = ScreenView {
            name: String::new(),
            seq: 0,
            cols: 0,
            rows: 0,
            cx: 0,
            cy: 0,
            off: 0,
            history: 0,
            cursor_shape: 0,
            cursor_blink: false,
            app_mouse: false,
            app_drag: false,
            alt_screen: false,
            title: String::new(),
            request_id: 0,
            lines: Vec::new(),
        };
        let events = [
            (
                Event::Capabilities {
                    capabilities: crate::runtime::Capabilities {
                        runtime: "native",
                        audio_playback: true,
                        clipboard: true,
                        desktop_open: true,
                        per_session_limits: true,
                        host_network_is_container: false,
                        host_terminals_are_container: false,
                        terminal: crate::runtime::TerminalCapabilities {
                            scrollback_lines: crate::config::SCROLLBACK_LINES,
                            min_cols: crate::shared::protocol::TERMINAL_MIN_COLS,
                            max_cols: crate::shared::protocol::TERMINAL_MAX_COLS,
                            min_rows: crate::shared::protocol::TERMINAL_MIN_ROWS,
                            max_rows: crate::shared::protocol::TERMINAL_MAX_ROWS,
                        },
                    },
                },
                "capabilities",
                "capabilities",
            ),
            (
                Event::Sessions {
                    sessions: Vec::new(),
                },
                "sessions",
                "sessions",
            ),
            (
                Event::Projects {
                    projects: Vec::new(),
                },
                "projects",
                "projects",
            ),
            (
                Event::Library {
                    library: Vec::new(),
                },
                "library",
                "library",
            ),
            (Event::Screen { screen }, "screen", "screen"),
            (
                Event::Usage {
                    usage: Default::default(),
                },
                "usage",
                "usage",
            ),
            (
                Event::Audio {
                    audio: Default::default(),
                },
                "audio",
                "audio",
            ),
            (
                Event::Jukebox {
                    jukebox: Default::default(),
                },
                "jukebox",
                "jukebox",
            ),
        ];

        for (event, tag, payload) in events {
            let value = serde_json::to_value(event).unwrap();
            assert_eq!(value["t"], tag);
            assert!(value.get(payload).is_some());
            assert_eq!(value.as_object().unwrap().len(), 2);
        }
    }

    #[test]
    fn file_action_paths_normalize_the_absolute_placeholder() {
        let path = normalize_path(Path::new("/tmp/slop/../repo/file name"));
        assert_eq!(path, PathBuf::from("/tmp/repo/file name"));
        assert_eq!(
            normalize_action_command(&path, "du -sh {{ absolute_path }}"),
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
    fn prompt_minimum_counts_unicode_characters() {
        assert!(prompt_is_long_enough("commit", 0));
        assert!(prompt_is_long_enough("12345678901234567890", 20));
        assert!(prompt_is_long_enough("áéíóú", 5));
        assert!(!prompt_is_long_enough("commit", 20));
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
    fn blank_rows_inside_the_tail_count_toward_its_limit() {
        let mut screen = String::from("  Do you want to make this edit?\n");
        screen.push_str(&"\n".repeat(TAIL_LINES - 1));
        screen.push_str("ordinary output\n");
        assert_eq!(match_rules(&seeded_rules(), &screen), None);
    }

    #[test]
    fn an_all_blank_screen_has_no_rule_match() {
        assert_eq!(match_rules(&seeded_rules(), "\n\n\n"), None);
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
            Input::Paste {
                bytes: b"hello".to_vec(),
            },
            Input::Bytes(b"cd".to_vec()),
            Input::Keys {
                keys: vec!["Enter".into()],
                literal: false,
            },
        ]);
        assert_eq!(batch.len(), 4);
        assert!(matches!(
            &batch[1],
            Input::Paste {
                bytes
            } if bytes == b"hello"
        ));
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
            process_running: false,
            seq: 0,
            retick_seq: 0,
            hash: 0,
            activity_hash: 0,
            last_change: 0,
            rule_cache: None,
            state_since: 0,
            bell: false,
            cols: BOOT_COLS,
            rows: BOOT_ROWS,
            plain: Arc::new(String::new()),
            screen: None,
            emu: None,
            reader: None,
            reader_token: None,
            input: None,
            breadcrumbs: Vec::new(),
            breadcrumbs_pending: false,
            auto_resume_pending: false,
            run_id: 0,
            title: TitleCapture::default(),
        }
    }

    #[test]
    fn slugs_are_names_tmux_accepts() {
        assert_eq!(slug("review diff"), "review-diff");
        assert_eq!(slug("run make test"), "run-make-test");
        assert_eq!(slug("v1.2 checks"), "v1-2-checks");
        assert_eq!(slug("  spaced  out  "), "spaced-out");
        assert_eq!(slug(" . "), "library");
        assert_eq!(slug(""), "library");

        for name in ["review diff", "v1.2 checks", "a/b", "", " . "] {
            assert!(check_name(&slug(name)).is_ok(), "slug of {name:?}");
        }
    }

    #[test]
    fn free_name_counts_up_past_config_live_and_host_tables() {
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

        cfg.host_terminals.push(crate::config::HostTerminalCfg {
            name: "review-diff-3".into(),
            ..Default::default()
        });
        assert_eq!(free_name(&live, &cfg, "review-diff"), "review-diff-4");
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

        let errand = LibraryItemCfg {
            name: "view-main-rs".into(),
            kind: LibraryItemKind::Shell,
            project: "repo".into(),
            command: Some("less -R -- /home/you/git/repo/main.rs".into()),
            text: String::new(),
            ..Default::default()
        };
        assert!(check_library_item(&cfg, &errand).is_err());

        let nowhere = LibraryItemCfg {
            project: String::new(),
            text: "hello".into(),
            ..errand.clone()
        };
        assert!(check_library_item(&cfg, &nowhere).is_err());

        let gone = LibraryItemCfg {
            project: "not-a-project".into(),
            text: "hello".into(),
            ..errand.clone()
        };
        assert!(check_library_item(&cfg, &gone).is_err());

        let fine = LibraryItemCfg {
            text: "hello".into(),
            ..errand
        };
        assert!(check_library_item(&cfg, &fine).is_ok());
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
        cfg.library.push(LibraryItemCfg {
            name: "tests".into(),
            kind: LibraryItemKind::Shell,
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

    #[test]
    fn json_patch_conversion_rejects_null_and_preserves_nested_values() {
        let value = json_to_toml(serde_json::json!({
            "enabled": true,
            "count": 3,
            "nested": ["one", false],
        }))
        .unwrap();
        assert_eq!(value["enabled"].as_bool(), Some(true));
        assert_eq!(value["count"].as_integer(), Some(3));
        assert_eq!(value["nested"][0].as_str(), Some("one"));
        assert_eq!(value["nested"][1].as_bool(), Some(false));
        assert!(json_to_toml(serde_json::Value::Null).is_err());
    }

    #[test]
    fn a_toml_table_patch_can_replace_a_scalar() {
        let mut base = toml::Value::String("old".into());
        merge_toml(
            &mut base,
            toml::toml! {
                replacement = "new"
            }
            .into(),
        );
        assert_eq!(base["replacement"].as_str(), Some("new"));
    }

    #[test]
    fn file_actions_stay_inside_the_project_root() {
        let project = ProjectCfg {
            name: "repo".into(),
            dir: "/tmp/slopworld-project".into(),
            ..Default::default()
        };
        assert_eq!(
            project_action_path(&project, "/tmp/slopworld-project/src/main.rs").unwrap(),
            PathBuf::from("/tmp/slopworld-project/src/main.rs")
        );
        let error = project_action_path(&project, "/tmp/slopworld-project-other/file")
            .unwrap_err()
            .to_string();
        assert!(error.contains("outside project"), "{error}");
    }

    #[test]
    fn check_mounts_rejects_unknown_projects() {
        use crate::config::{Mount, MountMode};

        let cfg = Config::parse(
            r#"
            [[project]]
            name = "main"
            dir = "/tmp"
            "#,
        )
        .unwrap();
        let s = SessionCfg {
            name: "a".into(),
            project: "main".into(),
            mounts: vec![Mount {
                project: "missing".into(),
                mode: MountMode::Ro,
            }],
            ..Default::default()
        };
        let err = super::check_mounts(&cfg, &s).unwrap_err().to_string();
        assert!(err.contains("missing"), "{err}");
    }

    #[test]
    fn check_project_rejects_unsafe_guest_aliases() {
        for name in ["../escape", "one/two", "/tmp/escape", ".", "..", r"one\two"] {
            let error = check_project(&ProjectCfg {
                name: name.into(),
                dir: "/tmp".into(),
                ..Default::default()
            })
            .unwrap_err()
            .to_string();
            assert!(error.contains("path component"), "{name:?}: {error}");
        }
    }

    #[test]
    fn mounts_round_trip_through_toml() {
        let cfg = Config::parse(
            r#"
            [[project]]
            name = "main"
            dir = "/tmp"

            [[project]]
            name = "lib"
            dir = "/tmp"

            [[session]]
            name = "a"
            project = "main"
            state_id = "44444444-4444-4444-8444-444444444444"

            [[session.mounts]]
            project = "lib"
            mode = "ro"
            "#,
        )
        .unwrap();

        let s = cfg.session("a").unwrap();
        assert_eq!(s.mounts.len(), 1);
        assert_eq!(s.mounts[0].project, "lib");
        assert_eq!(s.mounts[0].mode, crate::config::MountMode::Ro);
    }
}
