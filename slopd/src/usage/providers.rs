//! Provider credentials and HTTP requests. anthropic_cache owns shared request coordination.

use crate::clock::unix_ms;
use std::path::PathBuf;
use std::time::{Duration, SystemTime};

use serde_json::Value;
use ureq::{RequestBuilder, typestate::WithoutBody};

use super::Snapshot;
#[cfg(test)]
use crate::test_support as env;
#[cfg(not(test))]
use std::env;

#[cfg(test)]
#[path = "providers_tests.rs"]
mod tests;

const ANTHROPIC_USAGE_URL: &str = "https://api.anthropic.com/api/oauth/usage";
const OPENROUTER_CREDITS_URL: &str = "https://openrouter.ai/api/v1/credits";
const OPENAI_USAGE_URL: &str = "https://chatgpt.com/backend-api/wham/usage";
const ANTHROPIC_USER_AGENT: &str = concat!("claude-code/", env!("SLOPWORLD_VERSION"));
const OPENROUTER_KEY_ENV: &str = "OPENROUTER_API_KEY";
const ANTHROPIC_OAUTH_BETA: &str = "oauth-2025-04-20";
const REQUEST_TIMEOUT: Duration = Duration::from_secs(20);

pub(super) fn anthropic_usage_url() -> String {
    env::var("SLOPD_USAGE_URL").unwrap_or_else(|_| ANTHROPIC_USAGE_URL.to_string())
}

fn openrouter_credits_url() -> String {
    env::var("SLOPD_CREDITS_URL").unwrap_or_else(|_| OPENROUTER_CREDITS_URL.to_string())
}

fn openai_usage_url() -> String {
    env::var("SLOPD_OPENAI_USAGE_URL").unwrap_or_else(|_| OPENAI_USAGE_URL.to_string())
}

/// Read credentials for each poll and release them after the request.
/// Do not include the token in logs, configuration files, or daemon API responses.
pub(super) struct AnthropicCreds {
    pub(super) token: String,
    pub(super) plan: String,
    pub(super) path: PathBuf,
}

/// The two fields Codex needs to ask for the account's usage. The refresh token is
/// intentionally not read: polling must not be another actor that can rotate credentials.
pub(super) struct OpenAiCreds {
    token: String,
    account_id: Option<String>,
}

/// Provider-specific credentials passed from the credential reader to the request function.
/// This enum lets all providers use the same polling workflow.
pub(super) enum ProviderCredentials {
    Anthropic(AnthropicCreds),
    OpenRouter(String),
    OpenAi(OpenAiCreds),
}

/// Retry a failed JSON parse once after 50 ms.
/// Sandboxed Claude cannot rename a file over the shared credential bind mount.
/// It can instead truncate and rewrite the host inode, which temporarily leaves invalid JSON.
/// Return I/O errors immediately.
pub(super) fn read_anthropic_creds(path: &PathBuf) -> anyhow::Result<AnthropicCreds> {
    read_anthropic_creds_with_retry(path, || std::thread::sleep(Duration::from_millis(50)))
}

/// Keep retry ordering observable without relying on writer timing in tests.
fn read_anthropic_creds_with_retry(
    path: &PathBuf,
    before_retry: impl FnOnce(),
) -> anyhow::Result<AnthropicCreds> {
    match read_anthropic_creds_once(path) {
        Err(e) if e.downcast_ref::<serde_json::Error>().is_some() => {
            before_retry();
            read_anthropic_creds_once(path)
        }
        other => other,
    }
}

pub(super) fn read_anthropic_creds_once(path: &PathBuf) -> anyhow::Result<AnthropicCreds> {
    let text =
        std::fs::read_to_string(path).map_err(|e| anyhow::anyhow!("{}: {e}", path.display()))?;
    let v: Value = serde_json::from_str(&text)?;
    let o = v.get("claudeAiOauth").unwrap_or(&Value::Null);

    let token = o
        .get("accessToken")
        .and_then(Value::as_str)
        .ok_or_else(|| anyhow::anyhow!("no OAuth token in {}", path.display()))?
        .to_string();

    if let Some(why) = expiry_error(
        o.get("expiresAt").and_then(Value::as_u64),
        o.get("refreshTokenExpiresAt").and_then(Value::as_u64),
        unix_ms(),
    ) {
        anyhow::bail!("{why}");
    }

    Ok(AnthropicCreds {
        token,
        plan: o
            .get("subscriptionType")
            .and_then(Value::as_str)
            .unwrap_or_default()
            .to_string(),
        path: path.clone(),
    })
}

pub(super) fn read_openai_creds(path: &PathBuf) -> anyhow::Result<OpenAiCreds> {
    let text =
        std::fs::read_to_string(path).map_err(|e| anyhow::anyhow!("{}: {e}", path.display()))?;
    let v: Value = serde_json::from_str(&text)?;
    let token = v
        .get("tokens")
        .and_then(|tokens| tokens.get("access_token"))
        .and_then(Value::as_str)
        .filter(|v| !v.is_empty())
        .ok_or_else(|| anyhow::anyhow!("no ChatGPT access token in {}", path.display()))?
        .to_string();
    let account_id = v
        .get("tokens")
        .and_then(|tokens| tokens.get("account_id"))
        .and_then(Value::as_str)
        .filter(|v| !v.is_empty())
        .map(str::to_string);

    Ok(OpenAiCreds { token, account_id })
}

pub(super) fn read_anthropic(d: &crate::config::Daemon) -> anyhow::Result<ProviderCredentials> {
    let path = PathBuf::from(crate::config::expand(&d.claude_credentials));
    read_anthropic_creds(&path).map(ProviderCredentials::Anthropic)
}

pub(super) fn read_openrouter(d: &crate::config::Daemon) -> anyhow::Result<ProviderCredentials> {
    read_openrouter_key(&d.openrouter_key_file).map(ProviderCredentials::OpenRouter)
}

pub(super) fn read_openai(d: &crate::config::Daemon) -> anyhow::Result<ProviderCredentials> {
    let path = PathBuf::from(crate::config::expand(&d.openai_credentials));
    read_openai_creds(&path).map(ProviderCredentials::OpenAi)
}

/// Explain why polling cannot proceed. Timestamps are epoch milliseconds. An expired access
/// token only needs any host or agent Claude command. Claude refreshes it lazily into the
/// shared credential file. Only an expired refresh token requires `claude auth`.
pub(super) fn expiry_error(
    exp: Option<u64>,
    refresh_exp: Option<u64>,
    now: u64,
) -> Option<&'static str> {
    if !exp.is_some_and(|e| e < now) {
        return None;
    }
    if refresh_exp.is_some_and(|e| e < now) {
        return Some("Claude login expired. Run `claude auth` on the host.");
    }
    Some("The cached Claude token needs renewal. Run any `claude` command on the host or agent.")
}

/// Preserve the retry delay from a 429 response because Retry-After describes the server's requested wait.
pub(super) struct PollErr {
    pub(super) msg: String,
    /// Delay from Retry-After, or the minimum delay for a 429 response without that header.
    /// None for failures unrelated to rate limits.
    pub(super) retry_after: Option<u64>,
}

impl PollErr {
    pub(super) fn new(msg: impl std::fmt::Display) -> Self {
        PollErr {
            msg: msg.to_string(),
            retry_after: None,
        }
    }
}

impl std::fmt::Display for PollErr {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(&self.msg)
    }
}

pub(super) struct ProviderResponse {
    pub(super) body: Value,
    pub(super) plan: Option<String>,
}

pub(super) type ReadProvider = fn(&crate::config::Daemon) -> anyhow::Result<ProviderCredentials>;
pub(super) type FetchProvider = fn(ProviderCredentials) -> Result<ProviderResponse, PollErr>;
pub(super) type ParseProvider = fn(ProviderResponse) -> Snapshot;

/// What a 429 with no `Retry-After` is treated as asking for. A rate limit retried
/// a minute later is usually just another rate limit.
pub(super) const RATE_LIMIT_FLOOR_SECS: u64 = 300;

fn usage_request(url: String, token: &str, user_agent: &str) -> RequestBuilder<WithoutBody> {
    ureq::get(url)
        .config()
        .timeout_global(Some(REQUEST_TIMEOUT))
        // Preserve the response and Retry-After instead of turning status codes into errors.
        .http_status_as_error(false)
        .build()
        .header("Authorization", format!("Bearer {token}"))
        .header("User-Agent", user_agent)
}

fn fetch_json(
    request: RequestBuilder<WithoutBody>,
    rate_limit_message: &str,
    status_error: impl FnOnce(u16) -> String,
) -> Result<Value, PollErr> {
    let mut response = request.call().map_err(PollErr::new)?;
    let status = response.status().as_u16();
    if status == 429 {
        return Err(PollErr {
            msg: rate_limit_message.into(),
            retry_after: Some(rate_limit_delay(&response)),
        });
    }
    if !(200..300).contains(&status) {
        return Err(PollErr::new(status_error(status)));
    }
    response
        .body_mut()
        .read_json::<Value>()
        .map_err(PollErr::new)
}

pub(super) fn fetch_anthropic_usage(creds: &AnthropicCreds) -> Result<Value, PollErr> {
    let request = usage_request(anthropic_usage_url(), &creds.token, ANTHROPIC_USER_AGENT)
        .header("anthropic-beta", ANTHROPIC_OAUTH_BETA)
        .header("Accept", "application/json")
        .header("Content-Type", "application/json");
    fetch_json(
        request,
        "Anthropic is rate-limiting usage checks (429)",
        |status| {
            match status {
                // Only 401 identifies an expired login for Anthropic; 403 remains an endpoint error.
                401 => "Claude rejected the login (401). Is the host still signed in?".into(),
                _ => format!("usage endpoint returned {status}"),
            }
        },
    )
}

/// An empty path selects OPENROUTER_API_KEY from the slopd environment, as used by the pi preset.
/// This avoids storing a second copy of the key.
/// Otherwise, read the file for each poll and trim whitespace.
/// Do not log, save, or copy the key to another file.
pub(super) fn read_openrouter_key(file: &str) -> anyhow::Result<String> {
    if file.trim().is_empty() {
        let key = env::var(OPENROUTER_KEY_ENV).unwrap_or_default();
        if key.trim().is_empty() {
            anyhow::bail!(
                "${OPENROUTER_KEY_ENV} is missing from slopd's environment. Export it or specify a key file."
            );
        }
        return Ok(key.trim().to_string());
    }

    let path = crate::config::expand(file);
    let text = std::fs::read_to_string(&path).map_err(|e| anyhow::anyhow!("{path}: {e}"))?;
    let key = text.trim();
    if key.is_empty() {
        anyhow::bail!("{path} holds no key");
    }
    Ok(key.to_string())
}

pub(super) fn fetch_openrouter_credits(key: &str) -> Result<Value, PollErr> {
    let request = usage_request(
        openrouter_credits_url(),
        key,
        concat!("slopd/", env!("SLOPWORLD_VERSION")),
    );
    fetch_json(
        request,
        "OpenRouter is rate-limiting credit checks (429)",
        |status| match status {
            401 | 403 => format!("OpenRouter rejected the key ({status}). Is the key still valid?"),
            _ => format!("credits endpoint returned {status}"),
        },
    )
}

pub(super) fn fetch_openai_usage(creds: &OpenAiCreds) -> Result<Value, PollErr> {
    let mut request = usage_request(
        openai_usage_url(),
        &creds.token,
        concat!("slopd/", env!("SLOPWORLD_VERSION")),
    );
    if let Some(account_id) = &creds.account_id {
        request = request.header("ChatGPT-Account-Id", account_id);
    }
    fetch_json(
        request,
        "OpenAI is rate-limiting usage checks (429)",
        |status| match status {
            401 | 403 => {
                format!("OpenAI rejected the Codex login ({status}). Sign in with `codex login`.")
            }
            _ => format!("OpenAI usage endpoint returned {status}"),
        },
    )
}

pub(super) fn fetch_anthropic(creds: ProviderCredentials) -> Result<ProviderResponse, PollErr> {
    let ProviderCredentials::Anthropic(creds) = creds else {
        return Err(PollErr::new("internal Anthropic credential mismatch"));
    };
    super::anthropic_cache::fetch(&creds)
}

pub(super) fn fetch_openrouter(creds: ProviderCredentials) -> Result<ProviderResponse, PollErr> {
    let ProviderCredentials::OpenRouter(key) = creds else {
        return Err(PollErr::new("internal OpenRouter credential mismatch"));
    };
    fetch_openrouter_credits(&key).map(|body| ProviderResponse { body, plan: None })
}

pub(super) fn fetch_openai(creds: ProviderCredentials) -> Result<ProviderResponse, PollErr> {
    let ProviderCredentials::OpenAi(creds) = creds else {
        return Err(PollErr::new("internal OpenAI credential mismatch"));
    };
    fetch_openai_usage(&creds).map(|body| ProviderResponse { body, plan: None })
}

/// Parse either Retry-After format. A past HTTP date gives a valid zero-second delay.
/// `backoff` then applies the minimum rate-limit delay to prevent an immediate retry.
pub(super) fn retry_after<T>(res: &ureq::http::Response<T>) -> Option<u64> {
    let raw = res.headers().get("retry-after")?.to_str().ok()?;
    parse_retry_after(raw, SystemTime::now())
}

pub(super) fn rate_limit_delay<T>(res: &ureq::http::Response<T>) -> u64 {
    retry_after(res)
        .unwrap_or(RATE_LIMIT_FLOOR_SECS)
        .max(RATE_LIMIT_FLOOR_SECS)
}

pub(super) fn parse_retry_after(raw: &str, now: SystemTime) -> Option<u64> {
    let raw = raw.trim();
    let seconds = raw.parse::<u64>().ok().or_else(|| {
        let at = httpdate::parse_http_date(raw).ok()?;
        Some(
            at.duration_since(now)
                .map(|wait| wait.as_secs())
                .unwrap_or(0),
        )
    })?;
    Some(seconds.min(6 * 3600))
}
