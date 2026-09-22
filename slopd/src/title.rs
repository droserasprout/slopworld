//! Small, optional OpenRouter request used to name agent work.

use anyhow::{bail, Context, Result};
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::Mutex;
use std::time::Duration;

const URL: &str = "https://openrouter.ai/api/v1/chat/completions";
const KEY_ENV: &str = "OPENROUTER_API_KEY";
const MAX_PROMPT_CHARS: usize = 2000;
const MAX_TITLE_CHARS: usize = 60;
const REQUEST_ATTEMPTS: usize = 2;
const REQUEST_TIMEOUT: Duration = Duration::from_secs(8);
const CACHE_VERSION: u32 = 1;
const MAX_CACHE_ENTRIES: usize = 1024;

#[derive(Debug, Deserialize, Serialize)]
struct CacheFile {
    version: u32,
    entries: Vec<CacheEntry>,
    #[serde(default)]
    latest: Vec<LatestEntry>,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
struct CacheEntry {
    key: String,
    title: String,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
struct LatestEntry {
    session: String,
    title: String,
}

/// Persistent summaries are disposable runtime data and follow the XDG cache root.
pub fn cache_path(_config: &Path) -> PathBuf {
    crate::paths::cache_root().join("prompt-summaries.toml")
}

/// A small, best-effort cache for successful prompt summaries. The prompt itself is never
/// written: the key is a stable digest of the prompt and model, while the title is safe to
/// reuse across daemon and game restarts.
pub struct SummaryCache {
    path: PathBuf,
    state: Mutex<CacheState>,
}

struct CacheState {
    entries: Vec<CacheEntry>,
    latest: Vec<LatestEntry>,
}

impl SummaryCache {
    pub fn load(path: PathBuf) -> Self {
        let (entries, latest) = match fs::read_to_string(&path) {
            Ok(text) => match toml::from_str::<CacheFile>(&text) {
                Ok(file) if file.version == CACHE_VERSION => {
                    (trim_entries(file.entries), trim_latest(file.latest))
                }
                Ok(file) => {
                    tracing::warn!(
                        path = %path.display(),
                        version = file.version,
                        "ignoring unsupported prompt-summary cache"
                    );
                    (Vec::new(), Vec::new())
                }
                Err(error) => {
                    tracing::warn!(
                        path = %path.display(),
                        %error,
                        "ignoring invalid prompt-summary cache"
                    );
                    (Vec::new(), Vec::new())
                }
            },
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => (Vec::new(), Vec::new()),
            Err(error) => {
                tracing::warn!(
                    path = %path.display(),
                    %error,
                    "ignoring unreadable prompt-summary cache"
                );
                (Vec::new(), Vec::new())
            }
        };
        Self {
            path,
            state: Mutex::new(CacheState { entries, latest }),
        }
    }

    pub fn latest(&self, session: &str) -> Option<String> {
        self.state
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
            .latest
            .iter()
            .find(|entry| entry.session == session)
            .map(|entry| entry.title.clone())
    }

    pub fn get(&self, prompt: &str, summary_prompt: &str, model: &str) -> Option<String> {
        let key = cache_key(prompt, summary_prompt, model);
        let mut state = self
            .state
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        let at = state.entries.iter().position(|entry| entry.key == key)?;
        let entry = state.entries.remove(at);
        let title = entry.title.clone();
        state.entries.push(entry);
        Some(title)
    }

    pub fn insert(
        &self,
        session: &str,
        prompt: &str,
        summary_prompt: &str,
        model: &str,
        title: &str,
    ) -> Result<()> {
        let key = cache_key(prompt, summary_prompt, model);
        let mut state = self
            .state
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        insert_entry(&mut state.entries, key, title);
        set_latest(&mut state.latest, session, title);
        save_cache(&self.path, &state)
    }

    /// Store a summary that belongs to a durable task rather than a live session. It shares the
    /// prompt/instruction/model cache but deliberately does not add a session-title `latest`
    /// entry.
    pub fn insert_cached(
        &self,
        prompt: &str,
        summary_prompt: &str,
        model: &str,
        title: &str,
    ) -> Result<()> {
        let key = cache_key(prompt, summary_prompt, model);
        let mut state = self
            .state
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        insert_entry(&mut state.entries, key, title);
        save_cache(&self.path, &state)
    }

    pub fn remember(&self, session: &str, title: &str) -> Result<()> {
        let mut state = self
            .state
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        set_latest(&mut state.latest, session, title);
        save_cache(&self.path, &state)
    }

    pub fn clear_latest(&self, session: &str) -> Result<()> {
        let mut state = self
            .state
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        let before = state.latest.len();
        state.latest.retain(|entry| entry.session != session);
        if state.latest.len() == before {
            return Ok(());
        }
        save_cache(&self.path, &state)
    }
}

fn insert_entry(entries: &mut Vec<CacheEntry>, key: String, title: &str) {
    if let Some(at) = entries.iter().position(|entry| entry.key == key) {
        entries.remove(at);
    }
    entries.push(CacheEntry {
        key,
        title: title.to_string(),
    });
    if entries.len() > MAX_CACHE_ENTRIES {
        entries.remove(0);
    }
}

fn trim_entries(entries: Vec<CacheEntry>) -> Vec<CacheEntry> {
    entries
        .into_iter()
        .filter(|entry| !entry.key.is_empty() && !entry.title.trim().is_empty())
        .rev()
        .take(MAX_CACHE_ENTRIES)
        .collect::<Vec<_>>()
        .into_iter()
        .rev()
        .collect()
}

fn trim_latest(entries: Vec<LatestEntry>) -> Vec<LatestEntry> {
    let mut latest: Vec<LatestEntry> = Vec::new();
    for entry in entries
        .into_iter()
        .filter(|entry| !entry.session.is_empty() && !entry.title.trim().is_empty())
    {
        if let Some(at) = latest.iter().position(|old| old.session == entry.session) {
            latest.remove(at);
        }
        latest.push(entry);
    }
    if latest.len() > MAX_CACHE_ENTRIES {
        latest.drain(..latest.len() - MAX_CACHE_ENTRIES);
    }
    latest
}

fn set_latest(latest: &mut Vec<LatestEntry>, session: &str, title: &str) {
    latest.retain(|entry| entry.session != session);
    latest.push(LatestEntry {
        session: session.to_string(),
        title: title.to_string(),
    });
    if latest.len() > MAX_CACHE_ENTRIES {
        latest.remove(0);
    }
}

fn save_cache(path: &Path, state: &CacheState) -> Result<()> {
    let file = CacheFile {
        version: CACHE_VERSION,
        entries: state.entries.to_vec(),
        latest: state.latest.to_vec(),
    };
    crate::paths::write_private_toml(path, &toml::to_string_pretty(&file)?)
}

fn cache_key(prompt: &str, summary_prompt: &str, model: &str) -> String {
    let prompt: String = prompt.chars().take(MAX_PROMPT_CHARS).collect();
    let mut input = Vec::with_capacity(model.len() + summary_prompt.len() + prompt.len() + 32);
    input.extend_from_slice(b"slopworld-prompt-summary\0");
    input.extend_from_slice(model.as_bytes());
    input.push(0);
    input.extend_from_slice(summary_prompt.as_bytes());
    input.push(0);
    input.extend_from_slice(prompt.as_bytes());
    format!(
        "v{CACHE_VERSION}-{:016x}{:016x}",
        fnv(&input, 0xcbf29ce484222325),
        fnv(&input, 0x84222325cbf29ce4)
    )
}

fn fnv(bytes: &[u8], seed: u64) -> u64 {
    bytes.iter().fold(seed, |hash, byte| {
        (hash ^ u64::from(*byte)).wrapping_mul(0x100000001b3)
    })
}

fn endpoint() -> String {
    std::env::var("SLOPD_TITLE_URL").unwrap_or_else(|_| URL.into())
}

fn read_key(file: &str) -> Result<String> {
    let key = if file.trim().is_empty() {
        std::env::var(KEY_ENV).unwrap_or_default()
    } else {
        let path = crate::config::expand(file);
        std::fs::read_to_string(&path).with_context(|| format!("read {path}"))?
    };
    let key = key.trim();
    if key.is_empty() {
        bail!("no OpenRouter key configured")
    }
    Ok(key.to_string())
}

/// Run a summary off the async executor. Only join failures receive the caller's worker
/// label; provider errors pass through unchanged. Cache and policy decisions stay with callers.
pub async fn summarize_async(
    prompt: &str,
    summary_prompt: &str,
    key_file: &str,
    model: &str,
    worker: &str,
) -> Result<String> {
    let [prompt, summary_prompt, key_file, model] =
        [prompt, summary_prompt, key_file, model].map(str::to_owned);
    tokio::task::spawn_blocking(move || summarize(&prompt, &summary_prompt, &key_file, &model))
        .await
        .map_err(|error| anyhow::anyhow!("{worker}: {error}"))?
}

pub fn summarize(
    prompt: &str,
    summary_prompt: &str,
    key_file: &str,
    model: &str,
) -> Result<String> {
    let key = read_key(key_file)?;
    summarize_with_key(prompt, summary_prompt, &key, model, &endpoint())
}

fn summarize_with_key(
    prompt: &str,
    summary_prompt: &str,
    key: &str,
    model: &str,
    endpoint: &str,
) -> Result<String> {
    let prompt: String = prompt.chars().take(MAX_PROMPT_CHARS).collect();
    let instruction = if summary_prompt.trim().is_empty() {
        prompt
    } else {
        format!("{summary_prompt}\n\n{prompt}")
    };
    let body = json!({
        "model": model,
        "messages": [{"role": "user", "content": instruction}],
        // Keep enough room for a short answer even when the selected model reasons before
        // writing it. `max_tokens` is still accepted by OpenRouter, but is deprecated.
        "max_completion_tokens": 64,
        "reasoning": {"effort": "minimal", "exclude": true},
        "temperature": 0,
    });

    let mut last = None;
    for attempt in 0..REQUEST_ATTEMPTS {
        match request_once(&body, key, endpoint) {
            Ok(title) => return Ok(title),
            Err(AttemptError::Permanent(error)) => return Err(error),
            Err(AttemptError::Retry(error)) => {
                last = Some(error);
                if attempt + 1 < REQUEST_ATTEMPTS {
                    std::thread::sleep(Duration::from_millis(250));
                }
            }
        }
    }

    Err(last.unwrap_or_else(|| anyhow::anyhow!("OpenRouter title request failed")))
}

enum AttemptError {
    Retry(anyhow::Error),
    Permanent(anyhow::Error),
}

fn request_once(
    body: &Value,
    key: &str,
    endpoint: &str,
) -> std::result::Result<String, AttemptError> {
    let mut res = ureq::post(endpoint)
        .config()
        .timeout_global(Some(REQUEST_TIMEOUT))
        .http_status_as_error(false)
        .build()
        .header("Authorization", format!("Bearer {key}"))
        .header("Content-Type", "application/json")
        .header("User-Agent", concat!("slopd/", env!("SLOPWORLD_VERSION")))
        .send_json(body)
        .map_err(|e| AttemptError::Retry(anyhow::anyhow!(e)))?;
    let status = res.status().as_u16();
    if !(200..300).contains(&status) {
        let error = anyhow::anyhow!("OpenRouter title endpoint returned {status}");
        return if status == 408 || status == 425 || status == 429 || status >= 500 {
            Err(AttemptError::Retry(error))
        } else {
            Err(AttemptError::Permanent(error))
        };
    }

    let body: Value = res
        .body_mut()
        .read_json()
        .map_err(|e| AttemptError::Retry(anyhow::anyhow!(e)))?;
    let raw = message_content(&body).ok_or_else(|| {
        AttemptError::Permanent(anyhow::anyhow!("OpenRouter returned no title content"))
    })?;
    clean(&raw).ok_or_else(|| {
        AttemptError::Permanent(anyhow::anyhow!("OpenRouter returned an empty title"))
    })
}

fn message_content(body: &Value) -> Option<String> {
    let content = &body["choices"][0]["message"]["content"];
    if let Some(text) = content.as_str() {
        return Some(text.to_string());
    }

    let parts = content.as_array()?;
    let text: String = parts
        .iter()
        .filter_map(|part| part["text"].as_str())
        .collect();
    (!text.is_empty()).then_some(text)
}

fn clean(raw: &str) -> Option<String> {
    let trimmed = raw
        .trim()
        .trim_matches(|c| matches!(c, '"' | '\'' | '“' | '”'));
    let trimmed = trimmed.trim_end_matches(['.', '!', '?']);
    let title: String = trimmed.chars().take(MAX_TITLE_CHARS).collect();
    let title = title.trim();
    (!title.is_empty()).then(|| title.to_string())
}

#[cfg(test)]
#[path = "title_tests.rs"]
mod tests;
