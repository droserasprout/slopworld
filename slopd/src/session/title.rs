//! Own prompt composition, title policy, and per-session title transitions.
//! The capture manager coordinates requests; crate::title owns provider access and caching.

use super::*;
use crate::title::SummaryInput;

#[derive(Default)]
pub(super) struct TitleCapture {
    composer: Composer,
    // Identity belongs to this exact request, including across replacement sessions.
    pending: Option<Arc<()>>,
    // Once mode consumes an attempt even on failure to prevent repeated billable requests.
    once_requested: bool,
    override_title: Option<String>,
}

pub(super) struct TitleSettings {
    policy: TitlePolicy,
    minimum: usize,
    model: String,
    key_file: String,
    summary_prompt: String,
}

impl TitleSettings {
    pub(super) fn for_session(cfg: &Config, session: &SessionCfg, host: bool) -> Option<Self> {
        // Shell input and fixed-label sessions never enter the summarizer.
        if host
            || session
                .label
                .as_deref()
                .is_some_and(|label| !label.trim().is_empty())
        {
            return None;
        }
        let policy = match title_agent(cfg, session)? {
            TitleAgent::Codex => cfg.daemon.agent_titles,
            TitleAgent::Pi => cfg.daemon.pi_titles,
        };
        (policy != TitlePolicy::Never).then(|| Self {
            policy,
            minimum: cfg.daemon.title_min_chars,
            model: cfg.daemon.title_model.clone(),
            key_file: cfg.daemon.openrouter_key_file.clone(),
            summary_prompt: cfg.daemon.summary_prompt.clone(),
        })
    }

    pub(super) fn allows(&self, prompt: &str) -> bool {
        !is_dialog_answer(prompt) && prompt_is_long_enough(prompt, self.minimum)
    }
}

pub(super) enum TitleAction {
    Boundary,
    Request(TitleRequest),
}

pub(crate) struct TitleRequest {
    token: Arc<()>,
    pub(super) input: SummaryInput,
}

impl TitleCapture {
    pub(super) fn restored(title: Option<String>) -> Self {
        Self {
            once_requested: title.is_some(),
            override_title: title,
            ..Self::default()
        }
    }

    #[cfg(test)]
    pub(super) fn has_pending(&self) -> bool {
        self.pending.is_some()
    }

    pub(super) fn title(&self) -> Option<&str> {
        self.override_title.as_deref()
    }

    pub(super) fn reset(&mut self) {
        *self = Self::default();
    }

    pub(super) fn label_changed(&mut self, host: bool) {
        self.pending = None;
        if host {
            self.composer = Composer::ready();
            self.override_title = None;
        }
    }

    pub(super) fn disable(&mut self) -> bool {
        let changed = self.pending.is_some() || self.override_title.is_some();
        self.pending = None;
        self.composer = Composer::ready();
        self.override_title = None;
        changed
    }

    pub(super) fn accepts(&self, request: &TitleRequest) -> bool {
        self.pending
            .as_ref()
            .is_some_and(|token| Arc::ptr_eq(token, &request.token))
    }

    /// A stale completion must not clear the next request or replace its title.
    pub(super) fn finish(&mut self, request: &TitleRequest, title: Option<String>) -> bool {
        if !self.accepts(request) {
            return false;
        }
        self.pending = None;
        if let Some(title) = title {
            self.override_title = Some(title);
            true
        } else {
            false
        }
    }

    pub(super) fn paste(&mut self, text: &str) {
        self.composer.literal(text);
    }

    pub(super) fn capture_keys(
        &mut self,
        settings: &TitleSettings,
        keys: &[String],
        literal: bool,
    ) -> Option<TitleAction> {
        let (submission, _) = build_title_submission(&mut self.composer, keys, literal);
        match submission? {
            Submission::New(title) => {
                self.reset();
                self.override_title = title;
                Some(TitleAction::Boundary)
            }
            Submission::Prompt(prompt) => {
                if !settings.allows(&prompt)
                    || (settings.policy == TitlePolicy::Once && self.once_requested)
                {
                    return None;
                }
                if settings.policy == TitlePolicy::Once {
                    self.once_requested = true;
                }
                let token = Arc::new(());
                self.pending = Some(token.clone());
                Some(TitleAction::Request(TitleRequest {
                    token,
                    input: SummaryInput {
                        prompt,
                        summary_prompt: settings.summary_prompt.clone(),
                        key_file: settings.key_file.clone(),
                        model: settings.model.clone(),
                    },
                }))
            }
        }
    }
}

#[derive(Clone, Copy)]
enum TitleAgent {
    Codex,
    Pi,
}

// Explicit commands retain the same title behavior as their corresponding presets.
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

struct Composer {
    text: Vec<char>,
    cursor: usize,
    certain: bool,
}

enum Submission {
    Prompt(String),
    New(Option<String>),
}

// Approval answers never name work, regardless of terminal activity state.
fn is_dialog_answer(prompt: &str) -> bool {
    matches!(
        prompt.trim().to_ascii_lowercase().as_str(),
        "y" | "yes" | "n" | "no" | "1" | "2" | "a" | "b" | "ok" | "okay" | "cancel"
    )
}

fn prompt_is_long_enough(prompt: &str, minimum: usize) -> bool {
    prompt.chars().count() >= minimum
}

impl Default for Composer {
    fn default() -> Self {
        Self {
            text: Vec::new(),
            cursor: 0,
            certain: true,
        }
    }
}

impl Composer {
    fn char_before_cursor(&self) -> Option<char> {
        self.text.get(..self.cursor)?.last().copied()
    }

    fn char_at_cursor(&self) -> Option<char> {
        self.text.get(self.cursor).copied()
    }

    fn ready() -> Self {
        Self {
            certain: true,
            ..Self::default()
        }
    }

    fn literal(&mut self, text: &str) {
        // The mod encodes Shift+Enter with the kitty keyboard protocol.
        // Codex inserts a newline. Remove the escape bytes to keep the mirrored prompt consistent.
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

    fn key(&mut self, key: &str) -> Option<Submission> {
        match key {
            "Enter" => return self.submit(),
            // Codex's line editor accepts these readline-style aliases as well as the named
            // cursor keys emitted by the terminal window.
            "C-j" | "C-m" => return self.submit(),
            "BSpace" | "C-h" => {
                if self.cursor > 0 {
                    self.cursor -= 1;
                    self.text.remove(self.cursor);
                }
            }
            "DC" => self.delete_at_cursor(),
            "Left" | "C-b" => self.cursor = self.cursor.saturating_sub(1),
            "Right" | "C-f" => self.cursor = (self.cursor + 1).min(self.text.len()),
            // Ctrl-D on an empty prompt can close the editor rather than edit text.
            "C-d" if self.text.is_empty() => self.invalidate(),
            "C-d" => self.delete_at_cursor(),
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
                while self.cursor > 0 && self.char_before_cursor().is_some_and(char::is_whitespace)
                {
                    self.cursor -= 1;
                    self.text.remove(self.cursor);
                }
                while self.cursor > 0
                    && self
                        .char_before_cursor()
                        .is_some_and(|ch| !ch.is_whitespace())
                {
                    self.cursor -= 1;
                    self.text.remove(self.cursor);
                }
            }
            // An interrupt cancels the input rather than making the next Enter submit stale text.
            "C-c" => *self = Self::ready(),
            // History, completion, word movement, and terminal controls can invalidate the mirrored input.
            // Clear the mirror because it can no longer predict the text that Codex receives.
            // If Escape canceled the edit, the next prompt must not include that text.
            _ => self.invalidate(),
        }
        None
    }

    fn delete_at_cursor(&mut self) {
        if self.cursor < self.text.len() {
            self.text.remove(self.cursor);
        }
    }

    fn word_left(&mut self) {
        while self.cursor > 0 && self.char_before_cursor().is_some_and(char::is_whitespace) {
            self.cursor -= 1;
        }
        while self.cursor > 0
            && self
                .char_before_cursor()
                .is_some_and(|ch| !ch.is_whitespace())
        {
            self.cursor -= 1;
        }
    }

    fn word_right(&mut self) {
        while self.cursor < self.text.len()
            && self.char_at_cursor().is_some_and(char::is_whitespace)
        {
            self.cursor += 1;
        }
        while self.cursor < self.text.len()
            && self.char_at_cursor().is_some_and(|ch| !ch.is_whitespace())
        {
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

fn build_title_submission(
    composer: &mut Composer,
    keys: &[String],
    literal: bool,
) -> (Option<Submission>, bool) {
    let mut submission = None;
    if literal {
        for key in keys {
            composer.literal(key);
        }
    } else {
        for key in keys {
            if let Some(next) = composer.key(key) {
                submission = Some(next);
            }
        }
    }
    (submission, !composer.certain)
}

#[cfg(test)]
#[path = "title_tests.rs"]
mod tests;
