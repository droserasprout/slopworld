//! Small, optional OpenRouter request used to name Codex work from its submitted prompt.

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
}

#[derive(Clone, Debug, Deserialize, Serialize)]
struct CacheEntry {
    key: String,
    title: String,
}

/// Persistent summaries live beside the daemon config, which also keeps a custom
/// `SLOPD_CONFIG` installation self-contained.
pub fn cache_path(config: &Path) -> PathBuf {
    config.with_file_name("prompt-summaries.json")
}

/// A small, best-effort cache for successful prompt summaries. The prompt itself is never
/// written: the key is a stable digest of the prompt and model, while the title is safe to
/// reuse across daemon and game restarts.
pub struct SummaryCache {
    path: PathBuf,
    entries: Mutex<Vec<CacheEntry>>,
}

impl SummaryCache {
    pub fn load(path: PathBuf) -> Self {
        let entries = match fs::read_to_string(&path) {
            Ok(text) => match serde_json::from_str::<CacheFile>(&text) {
                Ok(file) if file.version == CACHE_VERSION => file
                    .entries
                    .into_iter()
                    .filter(|entry| !entry.key.is_empty() && !entry.title.trim().is_empty())
                    .rev()
                    .take(MAX_CACHE_ENTRIES)
                    .collect::<Vec<_>>()
                    .into_iter()
                    .rev()
                    .collect(),
                Ok(file) => {
                    tracing::warn!(
                        path = %path.display(),
                        version = file.version,
                        "ignoring unsupported prompt-summary cache"
                    );
                    Vec::new()
                }
                Err(error) => {
                    tracing::warn!(
                        path = %path.display(),
                        %error,
                        "ignoring invalid prompt-summary cache"
                    );
                    Vec::new()
                }
            },
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => Vec::new(),
            Err(error) => {
                tracing::warn!(
                    path = %path.display(),
                    %error,
                    "ignoring unreadable prompt-summary cache"
                );
                Vec::new()
            }
        };
        Self {
            path,
            entries: Mutex::new(entries),
        }
    }

    pub fn get(&self, prompt: &str, model: &str) -> Option<String> {
        let key = cache_key(prompt, model);
        let mut entries = self
            .entries
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        let at = entries.iter().position(|entry| entry.key == key)?;
        let entry = entries.remove(at);
        let title = entry.title.clone();
        entries.push(entry);
        Some(title)
    }

    pub fn insert(&self, prompt: &str, model: &str, title: &str) -> Result<()> {
        let key = cache_key(prompt, model);
        let mut entries = self
            .entries
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
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
        save_cache(&self.path, &entries)
    }
}

fn save_cache(path: &Path, entries: &[CacheEntry]) -> Result<()> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)?;
    }
    let tmp = path.with_extension("json.tmp");
    let file = CacheFile {
        version: CACHE_VERSION,
        entries: entries.to_vec(),
    };
    fs::write(&tmp, serde_json::to_vec_pretty(&file)?)?;
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        fs::set_permissions(&tmp, fs::Permissions::from_mode(0o600))?;
    }
    fs::rename(&tmp, path).with_context(|| format!("installing {}", path.display()))
}

fn cache_key(prompt: &str, model: &str) -> String {
    let prompt: String = prompt.chars().take(MAX_PROMPT_CHARS).collect();
    let mut input = Vec::with_capacity(model.len() + prompt.len() + 32);
    input.extend_from_slice(b"slopworld-prompt-summary\0");
    input.extend_from_slice(model.as_bytes());
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

pub fn summarize(prompt: &str, key_file: &str, model: &str) -> Result<String> {
    let key = read_key(key_file)?;
    summarize_with_key(prompt, &key, model, &endpoint())
}

fn summarize_with_key(prompt: &str, key: &str, model: &str, endpoint: &str) -> Result<String> {
    let prompt: String = prompt.chars().take(MAX_PROMPT_CHARS).collect();
    let instruction = format!(
        "Summarise this coding request in at most 6 words for a session title. \
         Reply with only the title, without quotes, punctuation, or commentary.\n\n{prompt}"
    );
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
        .header("User-Agent", concat!("slopd/", env!("CARGO_PKG_VERSION")))
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
mod tests {
    use super::{clean, message_content, summarize_with_key, SummaryCache};
    use serde_json::json;
    use std::fs;
    use std::io::{Read, Write};
    use std::net::{TcpListener, TcpStream};
    use std::thread;
    use std::time::Duration;

    fn mock_server(responses: Vec<(u16, &'static str)>) -> (String, thread::JoinHandle<()>) {
        let listener = TcpListener::bind("127.0.0.1:0").expect("bind mock title server");
        let endpoint = format!("http://{}", listener.local_addr().unwrap());
        let handle = thread::spawn(move || {
            for (status, response) in responses {
                let (mut stream, _) = listener.accept().expect("accept title request");
                let _ = read_request(&mut stream);
                let bytes = response.as_bytes();
                let reason = if status == 200 {
                    "OK"
                } else {
                    "Service Unavailable"
                };
                write!(
                    stream,
                    "HTTP/1.1 {status} {reason}\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{}",
                    bytes.len(),
                    response
                )
                .expect("write title response");
            }
        });
        (endpoint, handle)
    }

    fn read_request(stream: &mut TcpStream) -> Vec<u8> {
        stream
            .set_read_timeout(Some(Duration::from_secs(2)))
            .unwrap();
        let mut bytes = Vec::new();
        let mut chunk = [0u8; 1024];
        loop {
            match stream.read(&mut chunk) {
                Ok(0) | Err(_) => break,
                Ok(n) => {
                    bytes.extend_from_slice(&chunk[..n]);
                    if bytes.windows(4).any(|window| window == b"\r\n\r\n") {
                        break;
                    }
                }
            }
        }
        bytes
    }

    #[test]
    fn cleans_model_decoration_and_caps_length() {
        assert_eq!(
            clean("  \"Add Codex status titles.\"  ").as_deref(),
            Some("Add Codex status titles")
        );
        assert_eq!(clean("   "), None);
        assert_eq!(clean(&"x".repeat(80)).unwrap().chars().count(), 60);
    }

    #[test]
    fn accepts_string_and_text_block_content() {
        assert_eq!(
            message_content(&json!({
                "choices": [{"message": {"content": "Fix parser"}}]
            }))
            .as_deref(),
            Some("Fix parser")
        );
        assert_eq!(
            message_content(&json!({
                "choices": [{"message": {"content": [
                    {"type": "text", "text": "Fix "},
                    {"type": "text", "text": "parser"}
                ]}}]
            }))
            .as_deref(),
            Some("Fix parser")
        );
    }

    #[test]
    fn retries_a_transient_title_response() {
        let (endpoint, server) = mock_server(vec![
            (503, "{\"error\":{\"message\":\"busy\"}}"),
            (
                200,
                "{\"choices\":[{\"message\":{\"content\":\"Fix parser\"}}]}",
            ),
        ]);
        let title = summarize_with_key("fix the parser", "test-key", "test/model", &endpoint);
        server.join().unwrap();
        assert_eq!(title.unwrap(), "Fix parser");
    }

    #[test]
    fn summary_cache_round_trips_without_storing_the_prompt() {
        let path =
            std::env::temp_dir().join(format!("slopd-title-cache-{}.json", std::process::id()));
        let _ = fs::remove_file(&path);

        let cache = SummaryCache::load(path.clone());
        cache
            .insert("fix the parser", "test/model", "Fix parser")
            .unwrap();
        let raw = fs::read_to_string(&path).unwrap();
        assert!(!raw.contains("fix the parser"));
        assert_eq!(
            cache.get("fix the parser", "test/model").as_deref(),
            Some("Fix parser")
        );

        let restored = SummaryCache::load(path.clone());
        assert_eq!(
            restored.get("fix the parser", "test/model").as_deref(),
            Some("Fix parser")
        );
        assert!(restored.get("fix the parser", "other/model").is_none());
        let _ = fs::remove_file(path);
    }
}
