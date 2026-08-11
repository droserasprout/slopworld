//! Small, optional OpenRouter request used to name Codex work from its submitted prompt.

use anyhow::{bail, Context, Result};
use serde_json::{json, Value};
use std::time::Duration;

const URL: &str = "https://openrouter.ai/api/v1/chat/completions";
const KEY_ENV: &str = "OPENROUTER_API_KEY";
const MAX_PROMPT_CHARS: usize = 2000;
const MAX_TITLE_CHARS: usize = 60;

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
    let prompt: String = prompt.chars().take(MAX_PROMPT_CHARS).collect();
    let instruction = format!(
        "Summarise this coding request in at most 6 words for a session title. \
         Reply with only the title, without quotes, punctuation, or commentary.\n\n{prompt}"
    );
    let body = json!({
        "model": model,
        "messages": [{"role": "user", "content": instruction}],
        "max_tokens": 24,
        "temperature": 0,
    });
    let mut res = ureq::post(endpoint())
        .config()
        .timeout_global(Some(Duration::from_secs(5)))
        .http_status_as_error(false)
        .build()
        .header("Authorization", format!("Bearer {key}"))
        .header("Content-Type", "application/json")
        .header("User-Agent", concat!("slopd/", env!("CARGO_PKG_VERSION")))
        .send_json(body)?;
    let status = res.status().as_u16();
    if !(200..300).contains(&status) {
        bail!("OpenRouter title endpoint returned {status}")
    }
    let body: Value = res.body_mut().read_json()?;
    let raw = body["choices"][0]["message"]["content"]
        .as_str()
        .unwrap_or_default();
    clean(raw).ok_or_else(|| anyhow::anyhow!("OpenRouter returned an empty title"))
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
    use super::clean;

    #[test]
    fn cleans_model_decoration_and_caps_length() {
        assert_eq!(
            clean("  \"Add Codex status titles.\"  ").as_deref(),
            Some("Add Codex status titles")
        );
        assert_eq!(clean("   "), None);
        assert_eq!(clean(&"x".repeat(80)).unwrap().chars().count(), 60);
    }
}
