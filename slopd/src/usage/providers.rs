//! Provider credentials, HTTP requests, rate-limit handling, and shared Anthropic cache.

use crate::clock::unix_ms;
use std::fs::{File, OpenOptions};
use std::path::{Path, PathBuf};
use std::time::{Duration, Instant, SystemTime};

use serde::{Deserialize, Serialize};
use serde_json::Value;

use super::{Poller, Snapshot};

#[cfg(test)]
#[path = "providers_tests.rs"]
mod tests;

const USAGE_URL: &str = "https://api.anthropic.com/api/oauth/usage";
const CREDITS_URL: &str = "https://openrouter.ai/api/v1/credits";
const OPENAI_USAGE_URL: &str = "https://chatgpt.com/backend-api/wham/usage";
const ANTHROPIC_USER_AGENT: &str = concat!("claude-code/", env!("SLOPWORLD_VERSION"));
const KEY_ENV: &str = "OPENROUTER_API_KEY";
const OAUTH_BETA: &str = "oauth-2025-04-20";
const TIMEOUT: Duration = Duration::from_secs(20);
const USAGE_CACHE_TTL: Duration = Duration::from_secs(300);
const USAGE_CACHE_VERSION: u32 = 1;

fn usage_url() -> String {
    std::env::var("SLOPD_USAGE_URL").unwrap_or_else(|_| USAGE_URL.to_string())
}

fn credits_url() -> String {
    std::env::var("SLOPD_CREDITS_URL").unwrap_or_else(|_| CREDITS_URL.to_string())
}

fn openai_usage_url() -> String {
    std::env::var("SLOPD_OPENAI_USAGE_URL").unwrap_or_else(|_| OPENAI_USAGE_URL.to_string())
}

/// Read credentials for each poll and release them after the request.
/// Do not include the token in logs, configuration files, or daemon API responses.
pub(super) struct Creds {
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
    Anthropic(Creds),
    OpenRouter(String),
    OpenAi(OpenAiCreds),
}

/// Retry a failed JSON parse once after 50 ms.
/// Sandboxed Claude cannot rename a file over the shared credential bind mount.
/// It can instead truncate and rewrite the host inode, which temporarily leaves invalid JSON.
/// Return I/O errors immediately.
pub(super) fn read_creds(path: &PathBuf) -> anyhow::Result<Creds> {
    match read_creds_once(path) {
        Err(e) if e.downcast_ref::<serde_json::Error>().is_some() => {
            std::thread::sleep(std::time::Duration::from_millis(50));
            read_creds_once(path)
        }
        other => other,
    }
}

pub(super) fn read_creds_once(path: &PathBuf) -> anyhow::Result<Creds> {
    let text =
        std::fs::read_to_string(path).map_err(|e| anyhow::anyhow!("{}: {e}", path.display()))?;
    let v: Value = serde_json::from_str(&text)?;
    let o = &v["claudeAiOauth"];

    let token = o["accessToken"]
        .as_str()
        .ok_or_else(|| anyhow::anyhow!("no OAuth token in {}", path.display()))?
        .to_string();

    if let Some(why) = expiry_error(
        o["expiresAt"].as_u64(),
        o["refreshTokenExpiresAt"].as_u64(),
        unix_ms(),
    ) {
        anyhow::bail!("{why}");
    }

    Ok(Creds {
        token,
        plan: o["subscriptionType"]
            .as_str()
            .unwrap_or_default()
            .to_string(),
        path: path.clone(),
    })
}

pub(super) fn read_openai_creds(path: &PathBuf) -> anyhow::Result<OpenAiCreds> {
    let text =
        std::fs::read_to_string(path).map_err(|e| anyhow::anyhow!("{}: {e}", path.display()))?;
    let v: Value = serde_json::from_str(&text)?;
    let token = v["tokens"]["access_token"]
        .as_str()
        .filter(|v| !v.is_empty())
        .ok_or_else(|| anyhow::anyhow!("no ChatGPT access token in {}", path.display()))?
        .to_string();
    let account_id = v["tokens"]["account_id"]
        .as_str()
        .filter(|v| !v.is_empty())
        .map(str::to_string);

    Ok(OpenAiCreds { token, account_id })
}

pub(super) fn read_anthropic(d: &crate::config::Daemon) -> anyhow::Result<ProviderCredentials> {
    let path = PathBuf::from(crate::config::expand(&d.claude_credentials));
    read_creds(&path).map(ProviderCredentials::Anthropic)
}

#[derive(Debug, Deserialize, Serialize)]
struct AnthropicUsageCache {
    version: u32,
    fetched_ms: u64,
    body: Value,
    /// Set only after a 429, so another daemon can observe the same upstream backoff.
    #[serde(default)]
    retry_until_ms: Option<u64>,
}

/// Cache and lock files live under XDG's cache root. The credential path and endpoint are part of
/// the name so two configured accounts or a test proxy cannot reuse one another's answer.
fn anthropic_cache_paths(credentials: &Path) -> (PathBuf, PathBuf) {
    let mut hash = 0xcbf29ce484222325u64;
    for byte in credentials
        .to_string_lossy()
        .bytes()
        .chain([0u8])
        .chain(usage_url().bytes())
    {
        hash = (hash ^ u64::from(byte)).wrapping_mul(0x100000001b3);
    }

    let base = crate::paths::cache_root().join(format!(".anthropic-usage-{hash:016x}"));
    (base.with_extension("json"), base.with_extension("lock"))
}

pub(super) fn fresh_anthropic_cache(path: &Path) -> Option<Value> {
    let text = std::fs::read_to_string(path).ok()?;
    let cache = serde_json::from_str::<AnthropicUsageCache>(&text).ok()?;
    if cache.version != USAGE_CACHE_VERSION {
        return None;
    }
    if cache.body.is_null() {
        return None;
    }
    let age = unix_ms().checked_sub(cache.fetched_ms)?;
    (age < USAGE_CACHE_TTL.as_millis() as u64).then_some(cache.body)
}

pub(super) fn cached_anthropic_retry(path: &Path) -> Option<u64> {
    let text = std::fs::read_to_string(path).ok()?;
    let cache = serde_json::from_str::<AnthropicUsageCache>(&text).ok()?;
    let remaining_ms = cache.retry_until_ms?.checked_sub(unix_ms())?;
    (remaining_ms > 0).then(|| remaining_ms.saturating_add(999) / 1000)
}

/// Wait for the advisory lock while another daemon reads the cache or requests usage data.
/// Waiting up to the HTTP timeout avoids an additional request for the same account.
pub(super) fn lock_anthropic_cache(path: &Path) -> Option<nix::fcntl::Flock<File>> {
    let file = OpenOptions::new()
        .create(true)
        .truncate(false)
        .read(true)
        .write(true)
        .open(path)
        .ok()?;

    nix::fcntl::Flock::lock(file, nix::fcntl::FlockArg::LockExclusive).ok()
}

pub(super) fn save_anthropic_cache(path: &Path, body: &Value, retry_until_ms: Option<u64>) {
    let Some(parent) = path.parent() else { return };
    if std::fs::create_dir_all(parent).is_err() {
        return;
    }

    let tmp = path.with_extension("json.tmp");
    let cache = AnthropicUsageCache {
        version: USAGE_CACHE_VERSION,
        fetched_ms: unix_ms(),
        body: body.clone(),
        retry_until_ms,
    };
    let Ok(text) = serde_json::to_string(&cache) else {
        return;
    };
    if std::fs::write(&tmp, text).is_err() {
        return;
    }
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        let _ = std::fs::set_permissions(&tmp, std::fs::Permissions::from_mode(0o600));
    }
    let _ = std::fs::rename(tmp, path);
}

pub(super) fn save_anthropic_rate_limit(path: &Path, delay: u64) {
    let delay = delay.max(RATE_LIMIT_FLOOR);
    save_anthropic_cache(
        path,
        &Value::Null,
        Some(unix_ms().saturating_add(delay.saturating_mul(1_000))),
    );
}

pub(super) fn read_openrouter(d: &crate::config::Daemon) -> anyhow::Result<ProviderCredentials> {
    read_key(&d.openrouter_key_file).map(ProviderCredentials::OpenRouter)
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
    fn new(msg: impl std::fmt::Display) -> Self {
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
pub(super) type PrepareProvider =
    fn(&crate::config::Daemon, &mut Poller, &mut Option<SystemTime>, Instant);

/// What a 429 with no `Retry-After` is treated as asking for. A rate limit retried
/// a minute later is usually just another rate limit.
pub(super) const RATE_LIMIT_FLOOR: u64 = 300;

pub(super) fn fetch(creds: &Creds) -> Result<Value, PollErr> {
    let mut res = ureq::get(usage_url())
        .config()
        .timeout_global(Some(TIMEOUT))
        // Read status codes directly to preserve the response and its Retry-After header.
        // A ureq StatusCode error would discard them.
        .http_status_as_error(false)
        .build()
        .header("Authorization", format!("Bearer {}", creds.token))
        .header("anthropic-beta", OAUTH_BETA)
        .header("Accept", "application/json")
        .header("Content-Type", "application/json")
        .header("User-Agent", ANTHROPIC_USER_AGENT)
        .call()
        .map_err(PollErr::new)?;

    let status = res.status().as_u16();
    match status {
        // The only failure made worse by retrying at the usual rate.
        429 => {
            return Err(PollErr {
                msg: "Anthropic is rate-limiting usage checks (429)".into(),
                retry_after: Some(rate_limit_delay(&res)),
            })
        }
        // 401 means the token in the file is no longer good, which is a thing the user
        // fixes rather than waits out.
        401 => {
            return Err(PollErr::new(
                "Claude rejected the login (401). Is the host still signed in?",
            ))
        }
        s if !(200..300).contains(&s) => {
            return Err(PollErr::new(format!("usage endpoint returned {s}")))
        }
        _ => {}
    }

    res.body_mut().read_json::<Value>().map_err(PollErr::new)
}

/// An empty path selects OPENROUTER_API_KEY from the slopd environment, as used by the pi preset.
/// This avoids storing a second copy of the key.
/// Otherwise, read the file for each poll and trim whitespace.
/// Do not log, save, or copy the key to another file.
pub(super) fn read_key(file: &str) -> anyhow::Result<String> {
    if file.trim().is_empty() {
        let key = std::env::var(KEY_ENV).unwrap_or_default();
        if key.trim().is_empty() {
            anyhow::bail!(
                "${KEY_ENV} is missing from slopd's environment. Export it or specify a key file."
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

/// Request credit data separately from `fetch` because the endpoints use different headers and error messages.
pub(super) fn fetch_credits(key: &str) -> Result<Value, PollErr> {
    let mut res = ureq::get(credits_url())
        .config()
        .timeout_global(Some(TIMEOUT))
        .http_status_as_error(false)
        .build()
        .header("Authorization", format!("Bearer {key}"))
        .header("User-Agent", concat!("slopd/", env!("SLOPWORLD_VERSION")))
        .call()
        .map_err(PollErr::new)?;

    let status = res.status().as_u16();
    match status {
        429 => {
            return Err(PollErr {
                msg: "OpenRouter is rate-limiting credit checks (429)".into(),
                retry_after: Some(rate_limit_delay(&res)),
            })
        }
        // Something the user fixes rather than waits out, and the one failure worth naming the key
        // in. This is the only thing here that could be a stale copy in a file.
        401 | 403 => {
            return Err(PollErr::new(format!(
                "OpenRouter rejected the key ({status}). Is the key still valid?"
            )))
        }
        s if !(200..300).contains(&s) => {
            return Err(PollErr::new(format!("credits endpoint returned {s}")))
        }
        _ => {}
    }

    res.body_mut().read_json::<Value>().map_err(PollErr::new)
}

pub(super) fn fetch_openai(creds: &OpenAiCreds) -> Result<Value, PollErr> {
    let mut request = ureq::get(openai_usage_url())
        .config()
        .timeout_global(Some(TIMEOUT))
        .http_status_as_error(false)
        .build()
        .header("Authorization", format!("Bearer {}", creds.token))
        .header("User-Agent", concat!("slopd/", env!("SLOPWORLD_VERSION")));
    if let Some(account_id) = &creds.account_id {
        request = request.header("ChatGPT-Account-Id", account_id);
    }
    let mut res = request.call().map_err(PollErr::new)?;

    let status = res.status().as_u16();
    match status {
        429 => {
            return Err(PollErr {
                msg: "OpenAI is rate-limiting usage checks (429)".into(),
                retry_after: Some(rate_limit_delay(&res)),
            })
        }
        401 | 403 => {
            return Err(PollErr::new(format!(
                "OpenAI rejected the Codex login ({status}). Sign in with `codex login`."
            )))
        }
        s if !(200..300).contains(&s) => {
            return Err(PollErr::new(format!("OpenAI usage endpoint returned {s}")))
        }
        _ => {}
    }

    res.body_mut().read_json::<Value>().map_err(PollErr::new)
}

pub(super) fn fetch_anthropic(creds: ProviderCredentials) -> Result<ProviderResponse, PollErr> {
    let ProviderCredentials::Anthropic(creds) = creds else {
        return Err(PollErr::new("internal Anthropic credential mismatch"));
    };
    let plan = creds.plan.clone();
    let (cache_path, lock_path) = anthropic_cache_paths(&creds.path);

    if let Some(delay) = cached_anthropic_retry(&cache_path) {
        return Err(PollErr {
            msg: "Anthropic usage polling is in shared rate-limit backoff".into(),
            retry_after: Some(delay),
        });
    }
    if let Some(body) = fresh_anthropic_cache(&cache_path) {
        return Ok(ProviderResponse {
            body,
            plan: Some(plan),
        });
    }

    let lock = lock_anthropic_cache(&lock_path);
    if lock.is_some() {
        // A second daemon may have filled the cache while this one waited for the lock.
        if let Some(delay) = cached_anthropic_retry(&cache_path) {
            return Err(PollErr {
                msg: "Anthropic usage polling is in shared rate-limit backoff".into(),
                retry_after: Some(delay),
            });
        }
        if let Some(body) = fresh_anthropic_cache(&cache_path) {
            return Ok(ProviderResponse {
                body,
                plan: Some(plan),
            });
        }
    }

    let body = match fetch(&creds) {
        Ok(body) => body,
        Err(error) => {
            if let Some(delay) = error.retry_after {
                if lock.is_some() {
                    save_anthropic_rate_limit(&cache_path, delay);
                }
            }
            return Err(error);
        }
    };
    if lock.is_some() {
        save_anthropic_cache(&cache_path, &body, None);
    }
    Ok(ProviderResponse {
        body,
        plan: Some(plan),
    })
}

pub(super) fn fetch_openrouter(creds: ProviderCredentials) -> Result<ProviderResponse, PollErr> {
    let ProviderCredentials::OpenRouter(key) = creds else {
        return Err(PollErr::new("internal OpenRouter credential mismatch"));
    };
    fetch_credits(&key).map(|body| ProviderResponse { body, plan: None })
}

pub(super) fn fetch_openai_provider(
    creds: ProviderCredentials,
) -> Result<ProviderResponse, PollErr> {
    let ProviderCredentials::OpenAi(creds) = creds else {
        return Err(PollErr::new("internal OpenAI credential mismatch"));
    };
    fetch_openai(&creds).map(|body| ProviderResponse { body, plan: None })
}

/// Parse either Retry-After format. A past HTTP date gives a valid zero-second delay.
/// `backoff` then applies the minimum rate-limit delay to prevent an immediate retry.
pub(super) fn retry_after<T>(res: &ureq::http::Response<T>) -> Option<u64> {
    let raw = res.headers().get("retry-after")?.to_str().ok()?;
    parse_retry_after(raw, SystemTime::now())
}

pub(super) fn rate_limit_delay<T>(res: &ureq::http::Response<T>) -> u64 {
    retry_after(res)
        .unwrap_or(RATE_LIMIT_FLOOR)
        .max(RATE_LIMIT_FLOOR)
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
