//! Small, optional OpenRouter request used to name Codex work from its submitted prompt.

use anyhow::{bail, Context, Result};
use serde_json::{json, Value};
use std::time::Duration;

const URL: &str = "https://openrouter.ai/api/v1/chat/completions";
const KEY_ENV: &str = "OPENROUTER_API_KEY";
const MAX_PROMPT_CHARS: usize = 2000;
const MAX_TITLE_CHARS: usize = 60;
const REQUEST_ATTEMPTS: usize = 2;
const REQUEST_TIMEOUT: Duration = Duration::from_secs(8);

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
    use super::{clean, message_content, summarize_with_key};
    use serde_json::json;
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
}
