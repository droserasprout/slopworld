//! Title capture and prompt composition.

use super::*;

pub(super) struct TitleCapture {
    pub(super) composer: Composer,
    pub(super) conversation: u64,
    pub(super) generation: u64,
    pub(super) pending: bool,
    // Once mode counts the first request, not only a successful response. This keeps a
    // transient OpenRouter failure from turning every later prompt into another billable try.
    pub(super) once_requested: bool,
    pub(super) override_title: Option<String>,
}

#[derive(Clone, Copy)]
pub(super) enum TitleAgent {
    Codex,
    Pi,
}

/// The preset name is the usual case, but sessions may supply an explicit command line.
/// Those should retain the same title behavior as their corresponding preset.
pub(super) fn title_agent(cfg: &Config, session: &SessionCfg) -> Option<TitleAgent> {
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

pub(super) fn title_settings(
    cfg: &Config,
    session: &SessionCfg,
    host: bool,
) -> Option<(TitlePolicy, String)> {
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
    pub(super) fn once_available(&self) -> bool {
        !self.once_requested
    }

    pub(super) fn consume_once(&mut self) {
        self.once_requested = true;
    }
}

#[derive(Default)]
pub(super) struct Composer {
    text: Vec<char>,
    cursor: usize,
    pub(super) certain: bool,
}

pub(super) enum Submission {
    Prompt(String),
    New(Option<String>),
}

// Waiting screens normally consume a one-word approval or picker choice. Keep those out of
// OpenRouter, but do not discard a real prompt just because the last captured frame still says
// waiting while the agent's input line is active.
pub(super) fn is_dialog_answer(prompt: &str) -> bool {
    matches!(
        prompt.trim().to_ascii_lowercase().as_str(),
        "y" | "yes" | "n" | "no" | "1" | "2" | "a" | "b" | "ok" | "okay" | "cancel"
    )
}

pub(super) fn prompt_is_long_enough(prompt: &str, minimum: usize) -> bool {
    prompt.chars().count() >= minimum
}

impl Composer {
    pub(super) fn ready() -> Self {
        Self {
            certain: true,
            ..Self::default()
        }
    }

    pub(super) fn literal(&mut self, text: &str) {
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

    pub(super) fn paste(&mut self, text: &str) {
        self.literal(text);
    }

    pub(super) fn key(&mut self, key: &str) -> Option<Submission> {
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

pub(crate) struct TitleRequest {
    pub(super) prompt: String,
    pub(super) conversation: u64,
    pub(super) generation: u64,
    pub(super) key_file: String,
    pub(super) model: String,
}

pub(super) fn begin_title_request(
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
