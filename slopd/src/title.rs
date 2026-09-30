//! Shared summary resolution and OpenRouter transport.
//! Session eligibility belongs to session::title; persistence belongs to cache.

use anyhow::{bail, Context, Result};
use serde_json::{json, Value};
use std::time::Duration;

const URL: &str = "https://openrouter.ai/api/v1/chat/completions";
const KEY_ENV: &str = "OPENROUTER_API_KEY";
const MAX_PROMPT_CHARS: usize = 2000;
const MAX_TITLE_CHARS: usize = 60;
const REQUEST_ATTEMPTS: usize = 2;
const REQUEST_TIMEOUT: Duration = Duration::from_secs(8);
mod cache;
pub use cache::{cache_path, SummaryCache};

#[derive(Clone)]
pub(crate) struct SummaryInput {
    pub prompt: String,
    pub summary_prompt: String,
    pub key_file: String,
    pub model: String,
}

pub(crate) struct Summary {
    pub text: String,
    pub cache_hit: bool,
}

impl SummaryCache {
    /// Resolve without publishing: each caller must validate its own delayed result first.
    pub(crate) async fn resolve(&self, input: &SummaryInput) -> Result<Summary> {
        if let Some(text) = self.get(&input.prompt, &input.summary_prompt, &input.model) {
            return Ok(Summary {
                text,
                cache_hit: true,
            });
        }
        let text = summarize_async(
            &input.prompt,
            &input.summary_prompt,
            &input.key_file,
            &input.model,
            "summary worker",
        )
        .await?;
        Ok(Summary {
            text,
            cache_hit: false,
        })
    }

    /// Commit a validated summary; task summaries never create a session recovery entry.
    pub(crate) fn store(&self, input: &SummaryInput, summary: &Summary, session: Option<&str>) {
        match (session, summary.cache_hit) {
            (Some(name), true) => self.remember(name, &summary.text),
            (Some(name), false) => self.insert(
                name,
                &input.prompt,
                &input.summary_prompt,
                &input.model,
                &summary.text,
            ),
            (None, false) => self.insert_cached(
                &input.prompt,
                &input.summary_prompt,
                &input.model,
                &summary.text,
            ),
            (None, true) => {}
        }
    }
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

/// Generate a summary outside the asynchronous executor.
/// Add the caller's worker label only to join failures. Preserve provider errors unchanged.
/// Callers control caching and policy.
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
        let detail = provider_error(res.body_mut(), key);
        let error = anyhow::anyhow!("OpenRouter title endpoint returned {status}{detail}");
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

// Read only a bounded JSON error message; never include arbitrary bodies or request data.
fn provider_error(body: &mut ureq::Body, key: &str) -> String {
    use std::io::Read;
    let mut bytes = Vec::new();
    if body.as_reader().take(8192).read_to_end(&mut bytes).is_err() {
        return String::new();
    }
    let Ok(value) = serde_json::from_slice::<Value>(&bytes) else {
        return String::new();
    };
    let Some(message) = value.pointer("/error/message").and_then(Value::as_str) else {
        return String::new();
    };
    let message = message.replace(key, "[redacted]");
    let detail: String = message
        .chars()
        .filter(|c| !c.is_control())
        .take(512)
        .collect();
    if detail.trim().is_empty() {
        String::new()
    } else {
        format!(": {detail}")
    }
}

fn message_content(body: &Value) -> Option<String> {
    let content = body
        .get("choices")?
        .as_array()?
        .first()?
        .get("message")?
        .get("content")?;
    if let Some(text) = content.as_str() {
        return Some(text.to_string());
    }

    let parts = content.as_array()?;
    let text: String = parts
        .iter()
        .filter_map(|part| part.get("text").and_then(Value::as_str))
        .collect();
    (!text.is_empty()).then_some(text)
}

fn clean(raw: &str) -> Option<String> {
    let trimmed = raw.trim().trim_matches(|c: char| {
        c.is_whitespace() || matches!(c, '"' | '\'' | '“' | '”' | '.' | '!' | '?')
    });
    let title: String = trimmed.chars().take(MAX_TITLE_CHARS).collect();
    let title = title.trim();
    (!title.is_empty()).then(|| title.to_string())
}

#[cfg(test)]
#[path = "title_tests.rs"]
mod tests;
