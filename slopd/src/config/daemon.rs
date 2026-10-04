//! Daemon services, launch defaults, and host application settings.

use super::yes;
use serde::{Deserialize, Serialize};
use std::collections::{BTreeMap, BTreeSet};

/// Default HTTP listener address.
pub const DEFAULT_BIND: &str = "127.0.0.1:7717";
/// Global usage polling interval before provider minimums.
pub const DEFAULT_USAGE_POLL_SECS: u64 = 60;
/// Claude credential file, expanded when read.
pub const DEFAULT_CLAUDE_CREDENTIALS: &str = "~/.claude/.credentials.json";
/// Codex credential file, expanded when read.
pub const DEFAULT_OPENAI_CREDENTIALS: &str = "~/.codex/auth.json";
/// Default model for external titles and summaries.
pub const DEFAULT_TITLE_MODEL: &str = "google/gemini-3.1-flash-lite";
/// Minimum prompt length eligible for title generation.
pub const DEFAULT_TITLE_MIN_CHARS: usize = 0;
/// Default instruction for external title generation.
pub const DEFAULT_SUMMARY_PROMPT: &str = "Summarise this prompt in at most 6 words for a session title. Reply with only the title in sentence case, without quotes, punctuation, or commentary. If prompt is too short to summarize - return it verbatim.";

#[derive(Clone, Serialize, Deserialize)]
pub struct Daemon {
    // Listener and authentication.
    pub bind: String,
    /// Empty disables authentication. API reads redact it; writing the sentinel preserves it.
    #[serde(default)]
    pub token: String,

    // Usage polling and provider credentials.
    /// Global poll interval, subject to each provider's minimum.
    #[serde(default = "default_usage_poll")]
    pub usage_poll_secs: u64,
    /// Usage settings for each window. An explicit `interval_secs` overrides only the global interval.
    /// Source defaults apply until the source has configured rows.
    #[serde(default)]
    pub usage_items: BTreeMap<String, UsageItem>,
    /// Credential path; reread for each request to pick up token rotation.
    #[serde(default = "default_credentials")]
    pub claude_credentials: String,
    /// Reread this key file per request; empty selects `OPENROUTER_API_KEY`.
    #[serde(default)]
    pub openrouter_key_file: String,
    /// Codex credential path; reread per request. Credential contents stay off the API.
    #[serde(default = "default_openai_credentials")]
    pub openai_credentials: String,

    // External title and summary generation.
    /// Opt-in: sends prompt text to OpenRouter. `once` names the first prompt per conversation.
    #[serde(default)]
    pub agent_titles: TitlePolicy,
    /// The OpenRouter model used for automatic agent prompt titles.
    #[serde(default = "default_title_model")]
    pub title_model: String,
    /// Instruction prepended to prompts sent to OpenRouter for session and task summaries.
    #[serde(default = "default_summary_prompt")]
    pub summary_prompt: String,
    /// Minimum prompt length for an external title, in Unicode characters.
    #[serde(default = "default_title_min_chars")]
    pub title_min_chars: usize,
    /// Pi defaults to a title update for every prompt.
    #[serde(default = "default_pi_title_policy")]
    pub pi_titles: TitlePolicy,
    /// `once` summarizes each task once; `never` uses the local sidebar preview.
    #[serde(default)]
    pub task_summaries: TitlePolicy,

    // Delegated workers.
    /// Instructions delivered to newly spawned workers.
    #[serde(default)]
    pub instructions: InstructionsCfg,
    /// Allowed worker templates; catalog edits do not alter existing workers.
    #[serde(default, skip_serializing_if = "BTreeSet::is_empty")]
    pub worker_templates: BTreeSet<String>,
}

/// Settings for the prompt delivered to a new task worker.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct InstructionsCfg {
    /// Prompt submitted to a newly spawned task worker before it retrieves its mailbox task.
    #[serde(default = "default_worker_prompt")]
    pub worker_prompt: String,
}

/// Initial instructions for delegated task workers.
pub const DEFAULT_WORKER_PROMPT: &str = "You are a SlopWorld worker. Run `slopctl task show` once, then `slopctl task accept`. Use `slopctl task progress` while working and conclude with `slopctl task finish` or `slopctl task fail`. Do not search the task list or poll task status.\n\nWorker task: use `slopctl task show`, then `task accept`, `task progress`, and finally `task finish` or `task fail`. Do not search the task list or poll task status.";

fn default_worker_prompt() -> String {
    DEFAULT_WORKER_PROMPT.into()
}

impl Default for InstructionsCfg {
    fn default() -> Self {
        Self {
            worker_prompt: default_worker_prompt(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct UsageItem {
    /// Whether this individual usage window should be fetched and displayed.
    #[serde(default = "yes")]
    pub poll: bool,
    /// None inherits the global interval; provider minimums still apply.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub interval_secs: Option<u64>,
}

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum TitlePolicy {
    #[default]
    Never,
    Once,
    Always,
}

fn default_usage_poll() -> u64 {
    DEFAULT_USAGE_POLL_SECS
}

fn default_credentials() -> String {
    DEFAULT_CLAUDE_CREDENTIALS.into()
}

fn default_openai_credentials() -> String {
    DEFAULT_OPENAI_CREDENTIALS.into()
}

fn default_title_model() -> String {
    DEFAULT_TITLE_MODEL.into()
}

fn default_summary_prompt() -> String {
    DEFAULT_SUMMARY_PROMPT.into()
}

fn default_title_min_chars() -> usize {
    DEFAULT_TITLE_MIN_CHARS
}

fn default_pi_title_policy() -> TitlePolicy {
    TitlePolicy::Always
}

impl Default for Daemon {
    fn default() -> Self {
        Self {
            bind: DEFAULT_BIND.into(),
            token: String::new(),
            usage_poll_secs: default_usage_poll(),
            usage_items: BTreeMap::new(),
            claude_credentials: default_credentials(),
            openrouter_key_file: String::new(),
            openai_credentials: default_openai_credentials(),
            agent_titles: TitlePolicy::Never,
            title_model: default_title_model(),
            summary_prompt: default_summary_prompt(),
            title_min_chars: default_title_min_chars(),
            pi_titles: default_pi_title_policy(),
            task_summaries: TitlePolicy::Never,
            instructions: InstructionsCfg::default(),
            worker_templates: BTreeSet::new(),
        }
    }
}

/// Fallback agent and shell presets used when a launch omits them.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Defaults {
    /// The default command preset for agents without an explicit command.
    pub agent: String,
    /// The shell agents should advertise to tools that run commands inside the sandbox.
    #[serde(default = "default_agent_shell")]
    pub agent_shell: String,
    /// Default shell preset for shell errands.
    #[serde(default = "default_shell")]
    pub shell: String,
}

pub(crate) fn default_agent() -> String {
    "claude".into()
}

fn default_agent_shell() -> String {
    "bash".into()
}

fn default_shell() -> String {
    "bash".into()
}

impl Default for Defaults {
    fn default() -> Self {
        Self {
            agent: default_agent(),
            agent_shell: default_agent_shell(),
            shell: default_shell(),
        }
    }
}

/// Host applications that the mod uses for temporary file actions.
/// Split command templates into arguments without a shell.
/// The client expands `{file}` and `{line}` where supported.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CommandDefaults {
    /// Pager command, or `auto` to choose an installed tool on the daemon host.
    /// The client appends the file unless `{file}` is present.
    #[serde(default = "default_pager")]
    pub pager: String,
    /// Editor command. The client appends the file unless `{file}` is present.
    #[serde(default = "default_editor")]
    pub editor: String,
    /// Highlighter command, `auto` for installed tools, or empty to disable highlighting.
    /// In less's LESSOPEN hook, `%s` is replaced by less with the file path.
    #[serde(default = "default_highlighter")]
    pub highlighter: String,
}

fn default_pager() -> String {
    "auto".into()
}

fn default_editor() -> String {
    "micro".into()
}

fn default_highlighter() -> String {
    "auto".into()
}

impl Default for CommandDefaults {
    fn default() -> Self {
        Self {
            pager: default_pager(),
            editor: default_editor(),
            highlighter: default_highlighter(),
        }
    }
}

impl std::fmt::Debug for Daemon {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Daemon")
            .field("bind", &self.bind)
            .field("token", &super::TOKEN_REDACTED)
            .field("usage_poll_secs", &self.usage_poll_secs)
            .field("usage_items", &self.usage_items)
            .field("claude_credentials", &self.claude_credentials)
            .field("openrouter_key_file", &self.openrouter_key_file)
            .field("openai_credentials", &self.openai_credentials)
            .field("agent_titles", &self.agent_titles)
            .field("title_model", &self.title_model)
            .field("summary_prompt", &self.summary_prompt)
            .field("title_min_chars", &self.title_min_chars)
            .field("pi_titles", &self.pi_titles)
            .field("task_summaries", &self.task_summaries)
            .field("instructions", &self.instructions)
            .field("worker_templates", &self.worker_templates)
            .finish()
    }
}
