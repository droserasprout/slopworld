//! Longer planner completions share credentials and decoding with titles, not title limits.
use anyhow::{Result, bail};
use serde_json::json;
use std::time::Duration;

pub(crate) async fn complete_async(
    text: String,
    instruction: String,
    key_file: String,
    model: String,
) -> Result<String> {
    if model.trim().is_empty() || instruction.trim().is_empty() {
        bail!("Configure a Planner model and action prompt in Settings.");
    }
    tokio::task::spawn_blocking(move || {
        let key = super::read_key(&key_file)?;
        complete_with_key(&text, &instruction, &key, &model, &super::endpoint())
    })
    .await?
}

fn complete_with_key(
    text: &str,
    instruction: &str,
    key: &str,
    model: &str,
    endpoint: &str,
) -> Result<String> {
    let mut response = ureq::post(endpoint)
        .config()
        .timeout_global(Some(Duration::from_secs(60)))
        .http_status_as_error(false)
        .build()
        .header("Authorization", format!("Bearer {key}"))
        .send_json(json!({
            "model": model,
            "messages": [
                {"role": "system", "content": instruction},
                {"role": "user", "content": text}
            ],
            "max_completion_tokens": 4096
        }))?;
    let status = response.status().as_u16();
    if !(200..300).contains(&status) {
        let detail = super::provider_error(response.body_mut(), key);
        bail!("OpenRouter returned {status}{detail}");
    }
    let body: serde_json::Value = response.body_mut().read_json()?;
    if body
        .pointer("/choices/0/finish_reason")
        .and_then(serde_json::Value::as_str)
        == Some("length")
    {
        bail!("OpenRouter response was truncated; shorten the input or prompt.");
    }
    let output = super::message_content(&body).unwrap_or_default();
    if output.trim().is_empty() {
        bail!("OpenRouter returned no content.");
    }
    Ok(output)
}

#[cfg(test)]
#[path = "completion_tests.rs"]
mod tests;
