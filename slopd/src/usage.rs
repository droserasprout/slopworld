//! Polls provider usage with fresh credentials, narrow parsing, and snapshot-preserving failures; unknown responses yield no figures rather than false zeroes.

use std::collections::HashMap;
use std::fs::{File, OpenOptions};
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use serde::{Deserialize, Serialize};
use serde_json::Value;

use crate::session::Manager;

/// Undocumented and subject to change under us, which is why `parse` survives not
/// recognising what it gets and why `SLOPD_USAGE_URL` can point this elsewhere.
const USAGE_URL: &str = "https://api.anthropic.com/api/oauth/usage";

/// The OAuth endpoint is also used by Claude Code and is substantially less hostile when the
/// request identifies that client family. `SLOPWORLD_VERSION` is the daemon build identity, not
/// a Claude version, but the endpoint only needs a version-shaped value after this prefix.
const ANTHROPIC_USER_AGENT: &str = concat!("claude-code/", env!("SLOPWORLD_VERSION"));

fn usage_url() -> String {
    std::env::var("SLOPD_USAGE_URL").unwrap_or_else(|_| USAGE_URL.to_string())
}

/// Published, unlike the one above, and answering the same question in money:
/// `{"data":{"total_credits":n,"total_usage":n}}`.
const CREDITS_URL: &str = "https://openrouter.ai/api/v1/credits";

fn credits_url() -> String {
    std::env::var("SLOPD_CREDITS_URL").unwrap_or_else(|_| CREDITS_URL.to_string())
}

/// The same account-specific Codex usage the CLI shows. It is deliberately overrideable: it
/// is not a published API, and a proxy fixture must not require changing the daemon config.
const OPENAI_USAGE_URL: &str = "https://chatgpt.com/backend-api/wham/usage";

fn openai_usage_url() -> String {
    std::env::var("SLOPD_OPENAI_USAGE_URL").unwrap_or_else(|_| OPENAI_USAGE_URL.to_string())
}

/// The name the `pi` preset forwards, so a machine already running that agent has the key
/// where this can read it without a second copy in `config.toml`.
const KEY_ENV: &str = "OPENROUTER_API_KEY";

/// The beta header Claude Code's own OAuth calls carry. Without it the endpoint
/// treats the bearer as an API key and refuses it.
const OAUTH_BETA: &str = "oauth-2025-04-20";

const TIMEOUT: Duration = Duration::from_secs(20);
const USAGE_CACHE_TTL: Duration = Duration::from_secs(300);
const USAGE_CACHE_VERSION: u32 = 1;

/// Rides on the wire rather than being worked out from the key: the one thing this
/// must never do is let a percentage and a sum of money look alike.
#[derive(Debug, Clone, Copy, Serialize, PartialEq, Default)]
#[serde(rename_all = "lowercase")]
pub enum Unit {
    /// Percent of a window spent, which is every rate limit.
    #[default]
    Pct,
    /// Dollars spent, which is only ever the extra-usage budget.
    Usd,
}

/// Every rate-limit window is one of these, and so is the extra-usage budget -
/// same shape, told apart by `unit`.
#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct Window {
    pub key: String,
    pub label: String,
    /// 0-100, always present: a budget's percentage is true whether or not its dollars
    /// could be read.
    pub pct: f32,
    pub unit: Unit,
    /// Absent when the payload gave no figure to put a `$` on, which leaves the row a
    /// percentage.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub amount: Option<f32>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub limit: Option<f32>,
    /// Counted down by the mod against its own clock, so a stale snapshot still reads
    /// sensibly.
    pub resets_in: Option<u64>,
}

#[derive(Debug, Clone, Serialize, PartialEq, Default)]
pub struct Snapshot {
    /// False leaves `windows` as whatever the last good poll held, so the readout goes
    /// stale rather than empty while the network is out.
    pub ok: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub error: Option<String>,
    /// Straight from the credentials file: "pro", "max", ...
    pub plan: String,
    /// Unix millis of the last successful poll, so the mod can age the numbers.
    pub fetched_ms: u64,
    /// Which sellers are switched on, whether or not they answered. The mod draws a row per
    /// resource it *expects*, so a source that is down leaves its icons on the line with no
    /// number instead of taking them off the screen - which is the one thing a readout in the
    /// corner must not do, an icon that comes and goes being harder to read than an empty one.
    #[serde(skip_serializing_if = "Vec::is_empty")]
    pub sources: Vec<String>,
    /// Enabled sources whose last poll failed. Kept separate from `ok`: the latter describes
    /// the merged readout, while the mod needs to dim only the seller that is stale.
    #[serde(skip_serializing_if = "Vec::is_empty")]
    pub failed_sources: Vec<String>,
    pub windows: Vec<Window>,
}

impl Snapshot {
    fn failed(prev: &Snapshot, why: impl std::fmt::Display) -> Snapshot {
        Snapshot {
            ok: false,
            error: Some(why.to_string()),
            ..prev.clone()
        }
    }

    /// Compare only readout fields; fetched time ages values, while sources and failures
    /// preserve placeholder rows and provider-local stale state.
    pub fn same_readout(&self, other: &Snapshot) -> bool {
        self.ok == other.ok
            && self.error == other.error
            && self.plan == other.plan
            && self.failed_sources == other.failed_sources
            && self.windows == other.windows
            && self.sources == other.sources
    }
}

fn now_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0)
}

/// Read per poll and dropped immediately after the request; the token never
/// reaches a log line, the config file or the wire.
struct Creds {
    token: String,
    plan: String,
    path: PathBuf,
}

/// The two fields Codex needs to ask for the account's usage. The refresh token is
/// intentionally not read: polling must not be another actor that can rotate credentials.
struct OpenAiCreds {
    token: String,
    account_id: Option<String>,
}

/// The three credential shapes are different, but the poll loop only needs to pass one of
/// them from the provider's reader to its fetcher. Keeping that difference here leaves the
/// provider table below as data rather than three copies of the blocking workflow.
enum ProviderCredentials {
    Anthropic(Creds),
    OpenRouter(String),
    OpenAi(OpenAiCreds),
}

/// Retry one JSON parse after 50ms. A sandboxed Claude cannot rename over the shared
/// credential bind mount, so its fallback truncates and rewrites the host inode in place.
/// Readers can catch that brief invalid-JSON window. IO errors are persistent states and
/// return immediately.
fn read_creds(path: &PathBuf) -> anyhow::Result<Creds> {
    match read_creds_once(path) {
        Err(e) if e.downcast_ref::<serde_json::Error>().is_some() => {
            std::thread::sleep(std::time::Duration::from_millis(50));
            read_creds_once(path)
        }
        other => other,
    }
}

fn read_creds_once(path: &PathBuf) -> anyhow::Result<Creds> {
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
        now_ms(),
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

fn read_openai_creds(path: &PathBuf) -> anyhow::Result<OpenAiCreds> {
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

fn read_anthropic(d: &crate::config::Daemon) -> anyhow::Result<ProviderCredentials> {
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

/// Cache and lock files live beside the daemon config. The credential path and endpoint are part
/// of the name so two configured accounts or a test proxy cannot reuse one another's answer.
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

    let base = crate::config::Config::path_in_use()
        .with_file_name(format!(".anthropic-usage-{hash:016x}"));
    (base.with_extension("json"), base.with_extension("lock"))
}

fn fresh_anthropic_cache(path: &Path) -> Option<Value> {
    let text = std::fs::read_to_string(path).ok()?;
    let cache = serde_json::from_str::<AnthropicUsageCache>(&text).ok()?;
    if cache.version != USAGE_CACHE_VERSION {
        return None;
    }
    if cache.body.is_null() {
        return None;
    }
    let age = now_ms().checked_sub(cache.fetched_ms)?;
    (age < USAGE_CACHE_TTL.as_millis() as u64).then_some(cache.body)
}

fn cached_anthropic_retry(path: &Path) -> Option<u64> {
    let text = std::fs::read_to_string(path).ok()?;
    let cache = serde_json::from_str::<AnthropicUsageCache>(&text).ok()?;
    let remaining_ms = cache.retry_until_ms?.checked_sub(now_ms())?;
    (remaining_ms > 0).then(|| remaining_ms.saturating_add(999) / 1000)
}

/// A blocking advisory lock is intentional: another daemon holding it is either reading a fresh
/// cache or making the one upstream request, and waiting up to the HTTP timeout is cheaper than
/// making another account-level usage request.
fn lock_anthropic_cache(path: &Path) -> Option<nix::fcntl::Flock<File>> {
    let file = OpenOptions::new()
        .create(true)
        .truncate(false)
        .read(true)
        .write(true)
        .open(path)
        .ok()?;

    nix::fcntl::Flock::lock(file, nix::fcntl::FlockArg::LockExclusive).ok()
}

fn save_anthropic_cache(path: &Path, body: &Value, retry_until_ms: Option<u64>) {
    let Some(parent) = path.parent() else { return };
    if std::fs::create_dir_all(parent).is_err() {
        return;
    }

    let tmp = path.with_extension("json.tmp");
    let cache = AnthropicUsageCache {
        version: USAGE_CACHE_VERSION,
        fetched_ms: now_ms(),
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

fn save_anthropic_rate_limit(path: &Path, delay: u64) {
    let delay = delay.max(RATE_LIMIT_FLOOR);
    save_anthropic_cache(
        path,
        &Value::Null,
        Some(now_ms().saturating_add(delay.saturating_mul(1_000))),
    );
}

fn read_openrouter(d: &crate::config::Daemon) -> anyhow::Result<ProviderCredentials> {
    read_key(&d.openrouter_key_file).map(ProviderCredentials::OpenRouter)
}

fn read_openai(d: &crate::config::Daemon) -> anyhow::Result<ProviderCredentials> {
    let path = PathBuf::from(crate::config::expand(&d.openai_credentials));
    read_openai_creds(&path).map(ProviderCredentials::OpenAi)
}

/// Explain why polling cannot proceed. Timestamps are epoch milliseconds. An expired access
/// token only needs any host or agent Claude command; Claude refreshes it lazily into the
/// shared credential file. Only an expired refresh token requires `claude auth`.
fn expiry_error(exp: Option<u64>, refresh_exp: Option<u64>, now: u64) -> Option<&'static str> {
    if !exp.is_some_and(|e| e < now) {
        return None;
    }
    if refresh_exp.is_some_and(|e| e < now) {
        return Some("Claude login expired; run `claude auth` on the host");
    }
    Some("the cached Claude token needs renewing; run any `claude` command, host or agent")
}

/// The wait is carried rather than worked out: on a 429 the endpoint's own
/// `Retry-After` is the only thing anyone has been told about its limits.
struct PollErr {
    msg: String,
    /// From `Retry-After`, or the floor a 429 gets when it does not say. None for
    /// failures that are nobody's rate limit.
    retry_after: Option<u64>,
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

struct ProviderResponse {
    body: Value,
    plan: Option<String>,
}

type ReadProvider = fn(&crate::config::Daemon) -> anyhow::Result<ProviderCredentials>;
type FetchProvider = fn(ProviderCredentials) -> Result<ProviderResponse, PollErr>;
type ParseProvider = fn(ProviderResponse) -> Snapshot;
type PrepareProvider = fn(&crate::config::Daemon, &mut Poller, &mut Option<SystemTime>, Instant);

/// What a 429 with no `Retry-After` is treated as asking for. A rate limit retried
/// a minute later is usually just another rate limit.
const RATE_LIMIT_FLOOR: u64 = 300;

fn fetch(creds: &Creds) -> Result<Value, PollErr> {
    let mut res = ureq::get(usage_url())
        .config()
        .timeout_global(Some(TIMEOUT))
        // Statuses read rather than raised: ureq's `StatusCode` error has thrown the response
        // away by the time we see it, and with it a 429's `Retry-After`.
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
                "Claude rejected the login (401); is the host still signed in?",
            ))
        }
        s if !(200..300).contains(&s) => {
            return Err(PollErr::new(format!("usage endpoint returned {s}")))
        }
        _ => {}
    }

    res.body_mut().read_json::<Value>().map_err(PollErr::new)
}

/// Blank names slopd's own environment rather than a file: that is where the `pi` preset
/// forwards `OPENROUTER_API_KEY` from, so the machine that can already run the agent can
/// answer this without the key being written down a second time. A file is read fresh per
/// poll and trimmed, and neither road copies, logs or writes the key back.
fn read_key(file: &str) -> anyhow::Result<String> {
    if file.trim().is_empty() {
        let key = std::env::var(KEY_ENV).unwrap_or_default();
        if key.trim().is_empty() {
            anyhow::bail!("no ${KEY_ENV} in slopd's environment; export it or name a key file");
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

/// Same shape as `fetch`, and deliberately not folded together with it: what the two
/// endpoints share is four lines of status handling, and what they do not is every header
/// and every error sentence.
fn fetch_credits(key: &str) -> Result<Value, PollErr> {
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
        // Something the user fixes rather than waits out, and the one failure worth naming
        // the key in: this is the only thing here that could be a stale copy in a file.
        401 | 403 => {
            return Err(PollErr::new(format!(
                "OpenRouter rejected the key ({status}); is it still good?"
            )))
        }
        s if !(200..300).contains(&s) => {
            return Err(PollErr::new(format!("credits endpoint returned {s}")))
        }
        _ => {}
    }

    res.body_mut().read_json::<Value>().map_err(PollErr::new)
}

fn fetch_openai(creds: &OpenAiCreds) -> Result<Value, PollErr> {
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
                "OpenAI rejected the Codex login ({status}); sign in with `codex login`"
            )))
        }
        s if !(200..300).contains(&s) => {
            return Err(PollErr::new(format!("OpenAI usage endpoint returned {s}")))
        }
        _ => {}
    }

    res.body_mut().read_json::<Value>().map_err(PollErr::new)
}

fn fetch_anthropic(creds: ProviderCredentials) -> Result<ProviderResponse, PollErr> {
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

fn fetch_openrouter(creds: ProviderCredentials) -> Result<ProviderResponse, PollErr> {
    let ProviderCredentials::OpenRouter(key) = creds else {
        return Err(PollErr::new("internal OpenRouter credential mismatch"));
    };
    fetch_credits(&key).map(|body| ProviderResponse { body, plan: None })
}

fn fetch_openai_provider(creds: ProviderCredentials) -> Result<ProviderResponse, PollErr> {
    let ProviderCredentials::OpenAi(creds) = creds else {
        return Err(PollErr::new("internal OpenAI credential mismatch"));
    };
    fetch_openai(&creds).map(|body| ProviderResponse { body, plan: None })
}

/// Codex's usage answer has the account plan plus primary and secondary windows. The names are
/// deliberately ours: Anthropic has windows with the same cadence but they are separate pools.
/// Some plans put their only weekly window in `primary_window`, so use its reported cadence when
/// it is available rather than treating the field name as the window name.
fn parse_openai(v: &Value) -> Snapshot {
    let limits = if v["rate_limit"].is_object() {
        &v["rate_limit"]
    } else {
        v
    };
    let mut windows = Vec::new();
    for (field, fallback_key, fallback_label) in [
        ("primary_window", "openai_session", "session"),
        ("secondary_window", "openai_week", "weekly"),
    ] {
        let w = &limits[field];
        let Some(pct) = percent(w) else { continue };
        let (key, label) = if w["limit_window_seconds"].as_u64() == Some(7 * 24 * 60 * 60) {
            ("openai_week", "weekly")
        } else {
            (fallback_key, fallback_label)
        };
        windows.push(Window {
            key: key.into(),
            label: label.into(),
            pct,
            unit: Unit::Pct,
            amount: None,
            limit: None,
            resets_in: resets_in(w),
        });
    }

    let plan = v["plan_type"].as_str().unwrap_or_default().to_string();
    if windows.is_empty() {
        tracing::debug!("unrecognised OpenAI usage payload: {v}");
        return Snapshot {
            ok: false,
            error: Some("OpenAI answered in a shape slopd does not know".into()),
            plan,
            fetched_ms: now_ms(),
            ..Default::default()
        };
    }

    Snapshot {
        ok: true,
        plan,
        fetched_ms: now_ms(),
        windows,
        ..Default::default()
    }
}

fn parse_anthropic(response: ProviderResponse) -> Snapshot {
    parse(&response.body, response.plan.unwrap_or_default())
}

fn parse_openrouter(response: ProviderResponse) -> Snapshot {
    parse_credits(&response.body)
}

fn parse_openai_response(response: ProviderResponse) -> Snapshot {
    parse_openai(&response.body)
}

/// One row, in money. Both figures are wanted - a balance is a subtraction - and anything
/// else reads as "no numbers", for the reason `parse` does.
fn parse_credits(v: &Value) -> Snapshot {
    let d = v.get("data").and_then(Value::as_object);
    let used = d.and_then(|d| d.get("total_usage")).and_then(Value::as_f64);
    let credits = d
        .and_then(|d| d.get("total_credits"))
        .and_then(Value::as_f64);

    let (Some(used), Some(credits)) = (used, credits) else {
        tracing::debug!("unrecognised credits payload: {v}");
        return Snapshot {
            ok: false,
            error: Some(match used {
                // A key with no ceiling on it: the figure exists, there is just no balance
                // to count down. Said plainly rather than drawn as a full bar.
                Some(_) => "OpenRouter reports no credit limit on this key".into(),
                None => "OpenRouter answered in a shape slopd does not know".into(),
            }),
            ..Default::default()
        };
    };

    Snapshot {
        ok: true,
        error: None,
        plan: String::new(),
        fetched_ms: now_ms(),
        windows: vec![Window {
            key: "openrouter_balance".into(),
            label: "balance".into(),
            // Nothing bought is nothing left, which is 100% spent. Zero would draw a full
            // account beside an empty one.
            pct: if credits > 0.0 {
                ((used / credits) * 100.0).clamp(0.0, 100.0) as f32
            } else {
                100.0
            },
            unit: Unit::Usd,
            amount: Some(used as f32),
            limit: Some(credits as f32),
            // Credits are bought rather than granted, so there is no reset to count to.
            resets_in: None,
        }],
        ..Default::default()
    }
}

/// Parse either Retry-After form. A past HTTP date is a valid zero-second answer; `backoff`
/// applies the rate-limit floor rather than turning that into an immediate retry.
fn retry_after<T>(res: &ureq::http::Response<T>) -> Option<u64> {
    let raw = res.headers().get("retry-after")?.to_str().ok()?;
    parse_retry_after(raw, SystemTime::now())
}

fn rate_limit_delay<T>(res: &ureq::http::Response<T>) -> u64 {
    retry_after(res)
        .unwrap_or(RATE_LIMIT_FLOOR)
        .max(RATE_LIMIT_FLOOR)
}

fn parse_retry_after(raw: &str, now: SystemTime) -> Option<u64> {
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

/// Parse known window families and extra-usage money; unknown shapes produce an empty result rather than a false zero.
fn parse(v: &Value, plan: String) -> Snapshot {
    let mut windows = Vec::new();

    // Object order out of serde_json is alphabetical, which happens to be the order
    // these want reading in.
    if let Some(obj) = v.as_object() {
        for (name, w) in obj {
            let Some((key, label)) = family(name) else {
                continue;
            };
            // A window the plan does not have is null, which is not an error.
            if w.is_null() {
                continue;
            }
            let Some(pct) = percent(w) else { continue };
            windows.push(Window {
                key,
                label,
                pct,
                unit: Unit::Pct,
                amount: None,
                limit: None,
                resets_in: resets_in(w),
            });
        }
    }

    // Judged on the rate limits alone and before the money is added: an unrecognised payload
    // has to read as "no numbers" even if something in it was spend-shaped.
    if windows.is_empty() {
        tracing::debug!("unrecognised usage payload: {v}");
        return Snapshot {
            ok: false,
            error: Some("Anthropic answered in a shape slopd does not know".into()),
            plan,
            fetched_ms: now_ms(),
            windows,
            ..Default::default()
        };
    }

    // Last, because it is the one row that is not a rate limit and the readout draws
    // them in arrival order.
    windows.extend(spend(v));

    Snapshot {
        ok: true,
        error: None,
        plan,
        fetched_ms: now_ms(),
        windows,
        ..Default::default()
    }
}

/// Not part of `family`: `extra_usage` and `spend` both carry a `utilization`, and letting
/// either through the rate-limit path is how a quota row becomes a dollar row.
/// `monthly_limit` is minor units - 10000 is the $100 cap. A budget whose size cannot be read
/// still leaves a row, without an amount.
fn spend(v: &Value) -> Option<Window> {
    let e = &v["extra_usage"];
    if e.is_null() {
        return None;
    }

    // Off is not the same as nothing spent: a row reading $0 would say the account
    // had a budget.
    if e["is_enabled"].as_bool() == Some(false) {
        return None;
    }

    // `spend.percent` is the same number rounded, and stands in if the budget stops
    // reporting one.
    let pct = percent(e).or_else(|| {
        v["spend"]["percent"]
            .as_f64()
            .map(|p| p.clamp(0.0, 100.0) as f32)
    })?;

    let limit = budget(e);

    Some(Window {
        key: "claude_spend".into(),
        label: "extra usage".into(),
        pct,
        unit: if limit.is_some() {
            Unit::Usd
        } else {
            Unit::Pct
        },
        amount: limit.map(|l| l * pct / 100.0),
        limit,
        // Monthly, and the payload does not say when.
        resets_in: None,
    })
}

/// Named figures first, and the bare `monthly_limit` read as cents.
fn budget(e: &Value) -> Option<f32> {
    for k in ["monthly_limit_dollars", "limit_dollars"] {
        if let Some(d) = e[k].as_f64() {
            return Some(d as f32);
        }
    }
    e["monthly_limit"]
        .as_f64()
        .map(|cents| (cents / 100.0) as f32)
}

/// None for everything else in the payload, which is most of it.
fn family(name: &str) -> Option<(String, String)> {
    if name == "five_hour" {
        return Some(("claude_session".into(), "session".into()));
    }

    let rest = name.strip_prefix("seven_day")?;
    match rest.strip_prefix('_') {
        // Plain seven_day: the weekly limit itself.
        None => Some(("claude_week".into(), "week".into())),
        // seven_day_opus, seven_day_sonnet, and whatever comes next.
        Some(model) => Some((
            format!("claude_week_{model}"),
            format!("week ({})", model.replace('_', " ")),
        )),
    }
}

/// A percentage here - 52.0 means 52% - where the same figure rides the API's response
/// headers as a fraction. Guessing between them by size reads 0.8% spent as 80%.
fn percent(w: &Value) -> Option<f32> {
    for k in ["utilization", "used_pct", "used_percent", "percent_used"] {
        if let Some(p) = w[k].as_f64() {
            return Some(p.clamp(0.0, 100.0) as f32);
        }
    }

    let remaining = w["remaining"].as_f64()?;
    let limit = w["limit"].as_f64().filter(|l| *l > 0.0)?;
    Some((((limit - remaining) / limit) * 100.0).clamp(0.0, 100.0) as f32)
}

/// Absolute instants are converted here, so the mod never parses a date.
fn resets_in(w: &Value) -> Option<u64> {
    for k in ["resets_in_seconds", "resetsInSeconds"] {
        if let Some(s) = w[k].as_u64() {
            return Some(s);
        }
    }

    for k in ["resets_at", "resetsAt", "reset_at"] {
        // Epoch seconds.
        if let Some(at) = w[k].as_u64() {
            return Some(at.saturating_sub(now_ms() / 1000));
        }
        // RFC3339, parsed by hand rather than pulling in chrono for one field.
        if let Some(s) = w[k].as_str() {
            if let Some(at) = epoch_from_rfc3339(s) {
                return Some(at.saturating_sub(now_ms() / 1000));
            }
        }
    }
    None
}

/// `2026-07-26T09:59:59.621619+00:00` -> epoch seconds. All three forms this endpoint uses:
/// fractional seconds (dropped), a trailing `Z`, and a numeric offset, which is applied
/// rather than assumed zero.
fn epoch_from_rfc3339(s: &str) -> Option<u64> {
    let b = s.as_bytes();
    if b.len() < 19 || b[4] != b'-' || b[7] != b'-' || b[10] != b'T' {
        return None;
    }
    let n = |a: usize, z: usize| s[a..z].parse::<i64>().ok();
    let (y, mo, d) = (n(0, 4)?, n(5, 7)?, n(8, 10)?);
    let (h, mi, sec) = (n(11, 13)?, n(14, 16)?, n(17, 19)?);
    if !(1..=12).contains(&mo)
        || d < 1
        || d > days_in_month(y, mo)
        || !(0..=23).contains(&h)
        || !(0..=59).contains(&mi)
        || !(0..=60).contains(&sec)
    {
        return None;
    }

    // Days since the epoch, by the civil-from-days algorithm.
    let y = if mo <= 2 { y - 1 } else { y };
    let era = if y >= 0 { y } else { y - 399 } / 400;
    let yoe = y - era * 400;
    let mp = (mo + 9) % 12;
    let doy = (153 * mp + 2) / 5 + d - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    let days = era * 146_097 + doe - 719_468;

    u64::try_from(days * 86_400 + h * 3_600 + mi * 60 + sec - offset_secs(s)?).ok()
}

fn days_in_month(year: i64, month: i64) -> i64 {
    match month {
        2 if year % 4 == 0 && (year % 100 != 0 || year % 400 == 0) => 29,
        2 => 28,
        4 | 6 | 9 | 11 => 30,
        _ => 31,
    }
}

/// 0 for `Z` or a missing zone. None for a zone this cannot read, which fails the
/// whole timestamp rather than placing the reset in the wrong hour.
fn offset_secs(s: &str) -> Option<i64> {
    // Skip the date-time, and any fractional seconds after it.
    let zone = s[19..].trim_start_matches(|c: char| c == '.' || c.is_ascii_digit());

    if zone.is_empty() || zone == "Z" || zone == "z" {
        return Some(0);
    }

    let sign = match zone.as_bytes()[0] {
        b'+' => 1,
        b'-' => -1,
        _ => return None,
    };
    let rest = &zone[1..];
    if rest.len() != 5 || rest.as_bytes()[2] != b':' {
        return None;
    }
    let h = rest[0..2].parse::<i64>().ok()?;
    let m = rest[3..5].parse::<i64>().ok()?;
    if h > 23 || m > 59 {
        return None;
    }
    Some(sign * (h * 3_600 + m * 60))
}

/// The shortest window reported runs five hours, so half an hour behind is still worth
/// drawing. Also bounds how long a fixed login goes unnoticed, the loop only looking at the
/// config between sleeps.
const BACKOFF_CAP: u64 = 1800;

/// A 429 answered at the rate that earned it is a daemon feeding its own rate limit. The
/// endpoint has returned `Retry-After: 0` while continuing to reject requests, so every
/// rate-limited failure gets the same five-minute floor. A longer server answer still wins.
fn backoff(base: u64, fails: u32, asked: Option<u64>) -> u64 {
    let grown = base
        .saturating_mul(1u64 << fails.saturating_sub(1).min(16))
        .min(BACKOFF_CAP);
    grown
        .max(asked.unwrap_or(0))
        .max(if asked.is_some() { RATE_LIMIT_FLOOR } else { 0 })
}

fn human(secs: u64) -> String {
    if secs >= 3600 {
        format!("{}h{:02}m", secs / 3600, (secs % 3600) / 60)
    } else if secs >= 60 {
        format!("{}m", secs / 60)
    } else {
        format!("{secs}s")
    }
}

/// One seller, on its own clock. Two of these rather than one loop asking both: a source
/// that is rate-limiting us must not slow the other one down, and a key that has gone bad
/// must not take the other's numbers off the screen.
struct Poller {
    who: &'static str,
    snap: Snapshot,
    /// Any poll that comes back with numbers puts it back to zero.
    fails: u32,
    /// When this source is next due. Now, on the first lap and whenever it is switched back
    /// on, so a setting turned on in the GUI answers rather than serving out a backoff.
    due: Instant,
    /// Provider responses contain several windows, so each returned window keeps its own
    /// schedule even though the request itself is shared.
    item_due: HashMap<String, Instant>,
}

impl Poller {
    fn new(who: &'static str) -> Self {
        Poller {
            who,
            snap: Snapshot::default(),
            fails: 0,
            due: Instant::now(),
            item_due: HashMap::new(),
        }
    }

    /// Switched off. Its rows go and the other source's stay, which is the whole point of
    /// there being two of these. Answers whether anything actually changed.
    fn clear(&mut self) -> bool {
        self.fails = 0;
        self.due = Instant::now();
        self.item_due.clear();
        if self.snap == Snapshot::default() {
            return false;
        }
        self.snap = Snapshot::default();
        true
    }

    /// The delay, the two log edges and the wait written into the error the mod draws.
    fn settle(&mut self, mut next: Snapshot, asked: Option<u64>, base: u64) {
        let was_ok = self.snap.ok;

        let delay = if next.ok {
            self.fails = 0;
            base
        } else {
            self.fails = self.fails.saturating_add(1);
            backoff(base, self.fails, asked)
        };

        // Loud on either edge, because "is the readout live?" is otherwise a question
        // only the game can answer.
        match (&next.error, was_ok) {
            (Some(e), true) => tracing::warn!(
                "{} usage poll failed: {e}; next try in {}",
                self.who,
                human(delay)
            ),
            (Some(e), false) => {
                tracing::debug!("{} usage poll: {e}; next try in {}", self.who, human(delay))
            }
            (None, false) => tracing::info!(
                "{} usage: {}",
                self.who,
                next.windows
                    .iter()
                    .map(|w| match (w.unit, w.amount) {
                        (Unit::Usd, Some(a)) => format!("{} ${a:.2}", w.key),
                        _ => format!("{} {:.0}%", w.key, w.pct),
                    })
                    .collect::<Vec<_>>()
                    .join(", ")
            ),
            (None, true) => {}
        }

        // The readout draws this string, and "429" without "and I am not asking again for
        // ten minutes" reads as a daemon that has hung.
        if let Some(msg) = next.error.take() {
            next.error = Some(format!("{msg} - next try in {}", human(delay)));
        }

        self.snap = next;
        self.due = Instant::now() + Duration::from_secs(delay);
    }
}

struct Provider<'a> {
    source: &'static str,
    enabled: fn(&crate::config::Daemon) -> bool,
    poller: &'a mut Poller,
    read: ReadProvider,
    fetch: FetchProvider,
    parse: ParseProvider,
    /// Most providers need no work between laps. Anthropic alone watches its credential mtime.
    prepare: Option<PrepareProvider>,
}

fn anthropic_enabled(d: &crate::config::Daemon) -> bool {
    provider_enabled(d, "anthropic")
}

fn openrouter_enabled(d: &crate::config::Daemon) -> bool {
    provider_enabled(d, "openrouter")
}

fn openai_enabled(d: &crate::config::Daemon) -> bool {
    provider_enabled(d, "openai")
}

fn source_for_key(key: &str) -> Option<&'static str> {
    if key.starts_with("claude_") {
        Some("anthropic")
    } else if key.starts_with("openrouter_") {
        Some("openrouter")
    } else if key.starts_with("openai_") {
        Some("openai")
    } else {
        None
    }
}

fn default_source_enabled(source: &str) -> bool {
    matches!(source, "anthropic" | "openai")
}

fn item_enabled(d: &crate::config::Daemon, source: &str, key: &str) -> bool {
    if source_for_key(key) != Some(source) {
        return false;
    }
    d.usage_items
        .get(key)
        .map(|item| item.poll)
        .unwrap_or_else(|| default_source_enabled(source))
}

/// Once a table exists for a provider, turning every row off also turns the provider off. This
/// avoids keeping credentials and network polling alive for a table that visibly has no rows.
fn provider_enabled(d: &crate::config::Daemon, source: &str) -> bool {
    let rows: Vec<_> = d
        .usage_items
        .iter()
        .filter(|(key, _)| source_for_key(key) == Some(source))
        .collect();
    if rows.is_empty() {
        default_source_enabled(source)
    } else {
        rows.iter().any(|(_, item)| item.poll)
    }
}

fn minimum_poll_secs(source: &str) -> u64 {
    if source == "anthropic" {
        RATE_LIMIT_FLOOR
    } else {
        10
    }
}

fn item_interval(d: &crate::config::Daemon, source: &str, key: &str) -> u64 {
    d.usage_items
        .get(key)
        .and_then(|item| item.interval_secs)
        .filter(|seconds| *seconds > 0)
        .unwrap_or(d.usage_poll_secs)
        .max(minimum_poll_secs(source))
}

/// Providers answer several windows in one request. Poll at the fastest enabled row interval;
/// each row's toggle is still applied to the merged snapshot, while a slow row never makes a
/// faster row wait for it. Once the provider has answered, rows absent from that answer cannot
/// make the shared request faster (for example, an optional Claude model window may be null).
fn provider_interval(d: &crate::config::Daemon, source: &str, poller: &Poller) -> u64 {
    let has_answer = poller.snap.fetched_ms > 0;
    d.usage_items
        .iter()
        .filter(|(key, item)| {
            source_for_key(key) == Some(source)
                && item.poll
                && (!has_answer || poller.snap.windows.iter().any(|w| w.key == **key))
        })
        .map(|(key, _)| item_interval(d, source, key))
        .min()
        .unwrap_or(d.usage_poll_secs.max(minimum_poll_secs(source)))
}

fn filter_snapshot(
    poller: &mut Poller,
    mut snapshot: Snapshot,
    d: &crate::config::Daemon,
    source: &str,
    now: Instant,
) -> Snapshot {
    if !snapshot.ok {
        return snapshot;
    }

    poller
        .item_due
        .retain(|key, _| item_enabled(d, source, key));

    let previous = poller.snap.windows.clone();
    let mut seen = Vec::new();
    let mut windows = Vec::new();
    for window in snapshot.windows.drain(..) {
        if !item_enabled(d, source, &window.key) {
            continue;
        }

        let interval = item_interval(d, source, &window.key);
        let due = poller.item_due.entry(window.key.clone()).or_insert(now);
        if *due > now + Duration::from_secs(interval) {
            *due = now;
        }
        if *due <= now {
            *due = now + Duration::from_secs(interval);
            windows.push(window.clone());
        } else if let Some(old) = previous.iter().find(|old| old.key == window.key) {
            windows.push(old.clone());
        }
        seen.push(window.key);
    }

    // An optional window may disappear from a valid response. Keep its last value until that
    // row's own interval is due, then let it disappear instead of showing an old figure forever.
    for old in previous {
        if seen.iter().any(|key| key == &old.key) || !item_enabled(d, source, &old.key) {
            continue;
        }
        if poller.item_due.get(&old.key).is_some_and(|due| *due > now) {
            windows.push(old);
        }
    }
    snapshot.windows = windows;
    snapshot
}

fn preserve_parse_failure(previous: &Snapshot, parsed: Snapshot) -> Snapshot {
    if parsed.ok {
        parsed
    } else {
        Snapshot::failed(
            previous,
            parsed
                .error
                .unwrap_or_else(|| "provider returned no usable usage data".into()),
        )
    }
}

/// A login renewed on the host is the one thing that can turn "expired" back into numbers, and
/// the backoff a dead token earned is up to half an hour long - so the file is watched rather
/// than waited out. This is an optional provider hook because the other credential sources do
/// not have Claude's shared-file renewal behavior.
fn watch_anthropic_stamp(
    d: &crate::config::Daemon,
    poller: &mut Poller,
    creds_stamp: &mut Option<SystemTime>,
    now: Instant,
) {
    let creds_path = PathBuf::from(crate::config::expand(&d.claude_credentials));
    let stamp = std::fs::metadata(&creds_path)
        .and_then(|md| md.modified())
        .ok();
    if stamp != *creds_stamp {
        // Only while it is failing, and never on the first lap: this cuts a backoff short rather
        // than being a second way to ask. Claude Code rewrites that file whenever it refreshes a
        // token, and a healthy poller answering every rewrite would be a poll rate set by another
        // program.
        if creds_stamp.is_some() && poller.fails > 0 {
            poller.fails = 0;
            poller.due = now;
        }
        *creds_stamp = stamp;
    }
}

/// Read, fetch, parse and settle one provider. The descriptor supplies the provider-specific
/// functions; all scheduling, failure backoff and snapshot preservation stay here.
async fn poll_one(
    provider: &mut Provider<'_>,
    d: &crate::config::Daemon,
    now: Instant,
    base: u64,
) -> bool {
    if provider.poller.due > now {
        return false;
    }

    let prev = provider.poller.snap.clone();
    let config = d.clone();
    let read = provider.read;
    let fetch = provider.fetch;
    let parse = provider.parse;
    let (next, asked) = tokio::task::spawn_blocking(move || match read(&config) {
        Err(e) => (Snapshot::failed(&prev, e), None),
        Ok(creds) => match fetch(creds) {
            Err(e) => {
                let asked = e.retry_after;
                (Snapshot::failed(&prev, e), asked)
            }
            Ok(response) => (preserve_parse_failure(&prev, parse(response)), None),
        },
    })
    .await
    .unwrap_or_default();

    let next = filter_snapshot(provider.poller, next, d, provider.source, now);
    provider.poller.settle(next, asked, base);
    true
}

/// One list for the mod, which draws resources rather than sellers. `ok` is *every* live
/// source being current, because a row nobody could tell from a live one is the one thing
/// this must never draw; the errors are joined so the tooltip says which half is out. A
/// source that is off contributes nothing at all, so switching one off is not a failure.
fn merge<'a>(parts: impl IntoIterator<Item = (&'a str, &'a Snapshot)>) -> Snapshot {
    let mut out = Snapshot {
        ok: true,
        ..Default::default()
    };
    let mut errors = Vec::new();
    let mut any = false;

    for (source, p) in parts {
        if p.windows.is_empty() && p.error.is_none() {
            continue;
        }
        any = true;
        out.ok &= p.ok;
        out.fetched_ms = out.fetched_ms.max(p.fetched_ms);
        if out.plan.is_empty() {
            out.plan = p.plan.clone();
        }
        out.windows.extend(p.windows.iter().cloned());
        if !p.ok {
            out.failed_sources.push(source.to_string());
        }
        if let Some(e) = &p.error {
            errors.push(e.clone());
        }
    }

    // Nothing on: the default snapshot, which is what the readout draws nothing from.
    if !any {
        return Snapshot::default();
    }
    if !errors.is_empty() {
        out.error = Some(errors.join("; "));
    }
    out
}

/// How long a lap can be even with both sources due much later: what turns a source on is
/// `config.toml`, and this loop only looks at it between sleeps.
const LOOK: Duration = Duration::from_secs(30);

/// Started from main once, and quiet in the log unless something is wrong: this
/// runs every minute forever.
pub fn spawn(m: Arc<Manager>) -> tokio::task::JoinHandle<()> {
    tokio::spawn(async move {
        let mut anth = Poller::new("Anthropic");
        let mut cred = Poller::new("OpenRouter");
        let mut openai = Poller::new("OpenAI");
        let mut creds_stamp: Option<SystemTime> = None;

        loop {
            let cfg = m.config().await;
            let d = &cfg.daemon;
            let now = Instant::now();
            let mut moved = false;

            let mut providers = [
                Provider {
                    source: "anthropic",
                    enabled: anthropic_enabled,
                    poller: &mut anth,
                    read: read_anthropic,
                    fetch: fetch_anthropic,
                    parse: parse_anthropic,
                    prepare: Some(watch_anthropic_stamp),
                },
                Provider {
                    source: "openrouter",
                    enabled: openrouter_enabled,
                    poller: &mut cred,
                    read: read_openrouter,
                    fetch: fetch_openrouter,
                    parse: parse_openrouter,
                    prepare: None,
                },
                Provider {
                    source: "openai",
                    enabled: openai_enabled,
                    poller: &mut openai,
                    read: read_openai,
                    fetch: fetch_openai_provider,
                    parse: parse_openai_response,
                    prepare: None,
                },
            ];

            for provider in &mut providers {
                if let Some(prepare) = provider.prepare {
                    prepare(d, provider.poller, &mut creds_stamp, now);
                }

                if !(provider.enabled)(d) {
                    // Off is a setting that can be turned back on without a restart, so this
                    // drops the rows rather than returning.
                    moved |= provider.poller.clear();
                    continue;
                }

                // A row toggle should disappear from the wire immediately, even when this
                // provider is between network polls. Its next enabled poll will repopulate it.
                let before = provider.poller.snap.windows.len();
                provider
                    .poller
                    .item_due
                    .retain(|key, _| item_enabled(d, provider.source, key));
                provider
                    .poller
                    .snap
                    .windows
                    .retain(|window| item_enabled(d, provider.source, &window.key));
                moved |= before != provider.poller.snap.windows.len();

                let interval = provider_interval(d, provider.source, provider.poller);
                // A newly shortened interval should take effect on the next loop rather than
                // waiting out the old, longer due time. Increasing it naturally takes effect
                // after the next poll.
                if provider.poller.due > now + Duration::from_secs(interval) {
                    provider.poller.due = now;
                }

                moved |= poll_one(provider, d, now, interval).await;
            }

            // One event for both, and only when something actually moved: `set_usage`
            // re-announces to every client.
            if moved {
                let mut out = merge(
                    providers
                        .iter()
                        .map(|provider| (provider.source, &provider.poller.snap)),
                );
                // Said here rather than in `merge`, which knows about snapshots and not about
                // switches. A seller that is on but has never answered is exactly the case
                // this is for, so it goes on the list whatever its snapshot holds.
                out.sources.extend(
                    providers
                        .iter()
                        .filter(|provider| (provider.enabled)(d))
                        .map(|provider| provider.source.into()),
                );
                m.set_usage(out).await;
            }

            let due = providers
                .iter()
                .filter(|provider| (provider.enabled)(d))
                .map(|provider| provider.poller.due)
                .min();

            let wait = due
                .map(|at| at.saturating_duration_since(Instant::now()))
                .unwrap_or(LOOK)
                .min(LOOK)
                // A poll that came back instantly must not spin the loop on a due time
                // already in the past.
                .max(Duration::from_secs(1));

            tokio::time::sleep(wait).await;
        }
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Trimmed from what the endpoint answered on a Pro account.
    const REAL: &str = r#"{
        "five_hour": {"utilization": 52.0, "resets_at": "2100-07-26T09:59:59.621619+00:00",
                      "limit_dollars": null, "used_dollars": null},
        "seven_day": {"utilization": 55.0, "resets_at": "2100-07-26T19:59:59.621640+00:00"},
        "seven_day_oauth_apps": null,
        "seven_day_opus": null,
        "seven_day_sonnet": null,
        "limits": [],
        "extra_usage": {"is_enabled": true, "monthly_limit": 10000, "utilization": 20.93},
        "spend": {"percent": 21, "severity": "normal"},
        "member_dashboard_available": false
    }"#;

    #[test]
    fn usage_sources_use_rows_without_provider_switches() {
        let mut daemon = crate::config::Daemon::default();
        assert!(provider_enabled(&daemon, "anthropic"));
        assert!(!provider_enabled(&daemon, "openrouter"));
        assert!(provider_enabled(&daemon, "openai"));

        daemon.usage_items.insert(
            "openrouter_balance".into(),
            crate::config::UsageItem {
                poll: true,
                interval_secs: None,
            },
        );
        daemon.usage_items.insert(
            "claude_session".into(),
            crate::config::UsageItem {
                poll: false,
                interval_secs: None,
            },
        );
        assert!(provider_enabled(&daemon, "openrouter"));
        assert!(!provider_enabled(&daemon, "anthropic"));
        assert!(!item_enabled(&daemon, "anthropic", "claude_session"));
    }

    #[test]
    fn absent_anthropic_rows_do_not_set_the_provider_interval() {
        let mut daemon = crate::config::Daemon {
            usage_poll_secs: 120,
            ..Default::default()
        };
        daemon.usage_items.insert(
            "claude_session".into(),
            crate::config::UsageItem {
                poll: true,
                interval_secs: Some(600),
            },
        );
        daemon.usage_items.insert(
            "claude_week_opus".into(),
            crate::config::UsageItem {
                poll: true,
                interval_secs: Some(10),
            },
        );

        let mut poller = Poller::new("Anthropic");
        // Before the first answer, every configured row is eligible to request an initial value.
        assert_eq!(
            provider_interval(&daemon, "anthropic", &poller),
            RATE_LIMIT_FLOOR
        );

        poller.snap = parse(
            &serde_json::from_str(r#"{"five_hour":{"utilization":50}}"#).unwrap(),
            "max".into(),
        );
        // The optional Opus window was absent, so it cannot make the shared request poll faster.
        assert_eq!(provider_interval(&daemon, "anthropic", &poller), 600);
    }

    #[test]
    fn anthropic_cache_shares_successes_and_rate_limit_backoff() {
        let path = std::env::temp_dir().join(format!(
            "slopd-anthropic-cache-{}-{}.json",
            std::process::id(),
            now_ms()
        ));
        let body = serde_json::json!({"five_hour": {"utilization": 12}});

        save_anthropic_cache(&path, &body, None);
        assert_eq!(fresh_anthropic_cache(&path), Some(body));

        save_anthropic_rate_limit(&path, RATE_LIMIT_FLOOR);
        assert!(fresh_anthropic_cache(&path).is_none());
        assert!(cached_anthropic_retry(&path).is_some_and(|seconds| seconds > 0));

        std::fs::remove_file(path).unwrap();
    }

    #[test]
    fn reads_the_shape_the_endpoint_speaks() {
        let s = parse(&serde_json::from_str(REAL).unwrap(), "pro".into());

        assert!(s.ok);
        assert_eq!(s.windows.len(), 3, "null windows are absent, not zero");
        assert_eq!(s.windows[0].key, "claude_session");
        assert_eq!(s.windows[0].pct, 52.0);
        assert_eq!(s.windows[1].key, "claude_week");
        assert_eq!(s.windows[1].pct, 55.0);
        assert!(s.windows[0].resets_in.unwrap() > 0);
    }

    /// The money has a `utilization` too, so it must never come through as a rate
    /// limit. `monthly_limit` is cents: 10000 is the $100 cap.
    #[test]
    fn spend_is_money_and_not_a_rate_limit() {
        let s = parse(&serde_json::from_str(REAL).unwrap(), String::new());

        let rates = s.windows.iter().filter(|w| w.key != "claude_spend");
        assert!(rates
            .clone()
            .all(|w| w.unit == Unit::Pct && w.amount.is_none()));
        assert!(rates.map(|w| w.pct).all(|p| p != 20.93 && p != 21.0));

        let m = s.windows.last().unwrap();
        assert_eq!(m.key, "claude_spend");
        assert_eq!(m.unit, Unit::Usd);
        assert_eq!(m.limit, Some(100.0));
        assert!((m.amount.unwrap() - 20.93).abs() < 0.01);
    }

    /// A budget whose size cannot be read still leaves a row; it just stays a
    /// percentage.
    #[test]
    fn spend_without_a_readable_budget_stays_a_percentage() {
        let v: Value = serde_json::from_str(
            r#"{"five_hour":{"utilization":1},"extra_usage":{"is_enabled":true},
                "spend":{"percent":21}}"#,
        )
        .unwrap();

        let m = parse(&v, String::new()).windows.pop().unwrap();
        assert_eq!(m.key, "claude_spend");
        assert_eq!(m.unit, Unit::Pct);
        assert_eq!(m.pct, 21.0);
        assert!(m.amount.is_none());
    }

    /// The file has no atomic writer any more: inside a sandbox the rename fails with `EBUSY`
    /// and Claude Code truncates the host's inode in place instead, so a poll can read a file
    /// mid-write. Half-written is a *parse* error, and one retry is what keeps a microsecond
    /// window from costing half an hour of a wrong readout.
    #[test]
    fn a_half_written_credentials_file_is_read_again_rather_than_failed() {
        let path = std::env::temp_dir().join(format!("slopd-torn-{}.json", std::process::id()));
        let whole = r#"{"claudeAiOauth":{"accessToken":"t","subscriptionType":"max"}}"#;

        // The state O_TRUNC leaves behind, before the write lands.
        std::fs::write(&path, "").unwrap();
        let writer = {
            let path = path.clone();
            std::thread::spawn(move || {
                std::thread::sleep(std::time::Duration::from_millis(10));
                std::fs::write(&path, whole).unwrap();
            })
        };
        let got = read_creds(&path);
        writer.join().unwrap();
        assert_eq!(
            got.unwrap().token,
            "t",
            "the retry did not pick up the write"
        );

        // And a file that is still not JSON once the window has passed is a real error, not
        // something to keep quiet about.
        std::fs::write(&path, "half a {").unwrap();
        assert!(read_creds(&path).is_err());

        // A missing file is not retried at all - it is an IO error, and it will still be
        // missing in 50ms.
        std::fs::remove_file(&path).unwrap();
        assert!(read_creds(&path).is_err());
    }

    /// Both stamps are epoch milliseconds. Written as a real pair off a live credentials
    /// file, because the failure this guards is a unit slip and a synthetic `100 < 200`
    /// would survive one.
    #[test]
    fn expiry_reads_milliseconds() {
        // Issued 15:10 UTC-3, eight hours to run.
        let exp = 1_786_327_807_534;
        let refresh = 1_788_638_739_534;

        assert!(expiry_error(Some(exp), Some(refresh), exp - 1).is_none());
        assert!(expiry_error(Some(exp), Some(refresh), exp + 1).is_some());

        // The slip that started this: seconds either side of the comparison, and a token
        // three hours dead reads as good until the year 58,000.
        assert!(expiry_error(Some(exp), Some(refresh), exp / 1000).is_none());
        assert!(expiry_error(Some(exp * 1000), Some(refresh), exp + 10_800_000).is_none());
    }

    /// The whole point of telling them apart: an access token the host will renew on its
    /// own must not send anyone to `claude auth`.
    #[test]
    fn a_renewable_token_is_not_a_logged_out_host() {
        let now = 1_786_327_807_534;
        let stale = now - 1;
        let live_refresh = now + 30 * 86_400_000;

        let renew = expiry_error(Some(stale), Some(live_refresh), now).unwrap();
        assert!(renew.contains("needs renewing"));
        assert!(!renew.contains("claude auth"));

        let relogin = expiry_error(Some(stale), Some(stale), now).unwrap();
        assert!(relogin.contains("claude auth"));

        // No refresh stamp at all is not a dead one.
        assert!(!expiry_error(Some(stale), None, now)
            .unwrap()
            .contains("claude auth"));

        // Nothing said about expiry is not an expired token.
        assert!(expiry_error(None, Some(stale), now).is_none());
    }

    #[test]
    fn backoff_doubles_and_caps() {
        assert_eq!(backoff(60, 1, None), 60);
        assert_eq!(backoff(60, 2, None), 120);
        assert_eq!(backoff(60, 4, None), 480);
        // The shift saturates rather than overflowing on a long outage.
        assert_eq!(backoff(60, 30, None), BACKOFF_CAP);
    }

    /// `Retry-After` beats both the doubling and the cap, but may not make the poll
    /// faster than the rate-limit floor.
    #[test]
    fn retry_after_beats_the_guess() {
        assert_eq!(backoff(60, 1, Some(900)), 900);
        assert_eq!(backoff(60, 1, Some(30)), RATE_LIMIT_FLOOR);
        assert_eq!(backoff(60, 1, Some(0)), RATE_LIMIT_FLOOR);
        assert_eq!(backoff(60, 9, Some(7200)), 7200);
    }

    #[test]
    fn retry_after_parses_seconds_and_http_dates() {
        let now = UNIX_EPOCH + Duration::from_secs(1_000_000);

        assert_eq!(parse_retry_after(" 42 ", now), Some(42));
        assert_eq!(parse_retry_after("0", now), Some(0));
        assert_eq!(
            parse_retry_after(
                &httpdate::fmt_http_date(now + Duration::from_secs(123)),
                now
            ),
            Some(123)
        );
        assert_eq!(
            parse_retry_after(&httpdate::fmt_http_date(now - Duration::from_secs(1)), now),
            Some(0)
        );
        assert_eq!(parse_retry_after("not a retry delay", now), None);
        assert_eq!(parse_retry_after("999999", now), Some(6 * 3600));
    }

    #[test]
    fn the_unit_is_on_the_wire() {
        let s = parse(&serde_json::from_str(REAL).unwrap(), String::new());
        let json = serde_json::to_string(s.windows.last().unwrap()).unwrap();

        assert!(json.contains(r#""unit":"usd""#), "{json}");
        assert!(json.contains(r#""amount":20.93"#), "{json}");
        assert!(serde_json::to_string(&s.windows[0])
            .unwrap()
            .contains(r#""unit":"pct""#));
    }

    /// Extra usage switched off has no budget to draw.
    #[test]
    fn spend_off_is_not_spend_zero() {
        let v: Value = serde_json::from_str(
            r#"{"five_hour":{"utilization":1},
                "extra_usage":{"is_enabled":false,"monthly_limit":10000,"utilization":0}}"#,
        )
        .unwrap();

        assert!(parse(&v, String::new())
            .windows
            .iter()
            .all(|w| w.key != "claude_spend"));
    }

    /// The same figure is a fraction in the API's response headers, and guessing by
    /// size reads a window that is 0.8% spent as 80%.
    #[test]
    fn utilization_is_a_percentage_not_a_fraction() {
        let v: Value = serde_json::from_str(r#"{"five_hour":{"utilization":0.8}}"#).unwrap();
        assert_eq!(parse(&v, String::new()).windows[0].pct, 0.8);
    }

    /// Null on this plan and populated on others, so matched by family rather than by
    /// a list of names.
    #[test]
    fn per_model_weeks_come_through_named() {
        let v: Value = serde_json::from_str(
            r#"{"seven_day_opus":{"utilization":30},"seven_day_cowork":{"utilization":5}}"#,
        )
        .unwrap();

        let s = parse(&v, String::new());
        let keys: Vec<_> = s.windows.iter().map(|w| w.key.as_str()).collect();
        assert!(keys.contains(&"claude_week_opus") && keys.contains(&"claude_week_cowork"));
        assert_eq!(
            s.windows
                .iter()
                .find(|w| w.key == "claude_week_opus")
                .unwrap()
                .label,
            "week (opus)"
        );
    }

    #[test]
    fn remaining_over_limit_is_inverted() {
        let v: Value =
            serde_json::from_str(r#"{"seven_day":{"remaining":250,"limit":1000}}"#).unwrap();
        assert_eq!(parse(&v, String::new()).windows[0].pct, 75.0);
    }

    /// An unrecognised payload has to read as "no numbers", never as a colony sitting
    /// comfortably at zero.
    #[test]
    fn unknown_payload_is_not_zero_percent() {
        let v: Value = serde_json::from_str(r#"{"something_else":{"nope":1}}"#).unwrap();
        let s = parse(&v, String::new());
        assert!(!s.ok);
        assert!(s.windows.is_empty());
        assert!(s.error.is_some());
    }

    #[test]
    fn rfc3339_matches_a_known_instant() {
        // 2026-07-26T00:00:00Z, checked against `date -u -d @1785024000`.
        assert_eq!(epoch_from_rfc3339("2026-07-26T00:00:00Z"), Some(1785024000));
        assert_eq!(epoch_from_rfc3339("1970-01-01T00:00:00Z"), Some(0));
        assert_eq!(epoch_from_rfc3339("not a date"), None);
    }

    /// The three ways this endpoint has spelled the same instant.
    #[test]
    fn rfc3339_handles_fractions_and_offsets() {
        let z = epoch_from_rfc3339("2026-07-26T00:00:00Z").unwrap();
        assert_eq!(
            epoch_from_rfc3339("2026-07-26T00:00:00.621619+00:00"),
            Some(z)
        );
        assert_eq!(epoch_from_rfc3339("2026-07-26T00:00:00+00:00"), Some(z));
        // Midnight five hours west of UTC is 05:00 UTC.
        assert_eq!(
            epoch_from_rfc3339("2026-07-26T00:00:00-05:00"),
            Some(z + 5 * 3600)
        );
        // A zone this cannot read fails the timestamp rather than guessing UTC.
        assert_eq!(epoch_from_rfc3339("2026-07-26T00:00:00+0500"), None);
    }

    #[test]
    fn rfc3339_rejects_out_of_range_fields_and_trailing_zone_text() {
        for bad in [
            "2026-13-01T00:00:00Z",
            "2026-02-29T00:00:00Z",
            "2024-02-30T00:00:00Z",
            "2026-07-26T24:00:00Z",
            "2026-07-26T00:60:00Z",
            "2026-07-26T00:00:00+24:00",
            "2026-07-26T00:00:00+00:60",
            "2026-07-26T00:00:00+00:00garbage",
        ] {
            assert_eq!(epoch_from_rfc3339(bad), None, "accepted {bad}");
        }
        assert!(epoch_from_rfc3339("2024-02-29T00:00:00Z").is_some());
    }

    /// Credits bought less credits spent, in money, on the same wire as a rate limit.
    #[test]
    fn credits_are_a_balance_in_money() {
        let v: Value = serde_json::from_str(
            r#"{"data":{"total_credits":25.0,"total_usage":10.0,"total_usage_bkt":0}}"#,
        )
        .unwrap();

        let s = parse_credits(&v);
        assert!(s.ok);
        let w = &s.windows[0];
        assert_eq!(w.key, "openrouter_balance");
        assert_eq!(w.unit, Unit::Usd);
        assert_eq!(w.amount, Some(10.0));
        assert_eq!(w.limit, Some(25.0));
        assert_eq!(w.pct, 40.0);
        // Bought rather than granted: there is nothing to count down to.
        assert!(w.resets_in.is_none());
    }

    /// Nothing bought is nothing left. Zero percent spent would draw an empty account
    /// beside a full one.
    #[test]
    fn no_credits_is_spent_rather_than_untouched() {
        let v: Value =
            serde_json::from_str(r#"{"data":{"total_credits":0,"total_usage":0}}"#).unwrap();
        assert_eq!(parse_credits(&v).windows[0].pct, 100.0);
    }

    #[test]
    fn an_unknown_credits_payload_is_not_a_full_wallet() {
        let v: Value = serde_json::from_str(r#"{"data":{"nope":1}}"#).unwrap();
        let s = parse_credits(&v);
        assert!(!s.ok);
        assert!(s.windows.is_empty());
    }

    #[test]
    fn credits_require_the_current_nested_payload() {
        let v: Value = serde_json::from_str(r#"{"total_credits":10.0,"total_usage":3.5}"#).unwrap();
        let s = parse_credits(&v);
        assert!(!s.ok);
        assert!(s.windows.is_empty());
    }

    /// The mod draws resources, not sellers: three sources are one list.
    #[test]
    fn sources_merge_into_one_list() {
        let a = parse(&serde_json::from_str(REAL).unwrap(), "max".into());
        let c = parse_credits(
            &serde_json::from_str(r#"{"data":{"total_credits":25,"total_usage":10}}"#).unwrap(),
        );

        let o = parse_openai(
            &serde_json::from_str(r#"{"rate_limit":{"primary_window":{"used_percent":20}}}"#)
                .unwrap(),
        );
        let m = merge([("anthropic", &a), ("openrouter", &c), ("openai", &o)]);
        assert!(m.ok);
        assert_eq!(m.windows.len(), a.windows.len() + 2);
        assert_eq!(m.windows.last().unwrap().key, "openai_session");
        // The plan is the subscription's, and only one source has one.
        assert_eq!(m.plan, "max");
        assert!(m.error.is_none());
    }

    /// Turning a seller off is a change the mod has to hear about even when every figure it
    /// was already drawing stayed put: it draws a row per *expected* source, so a snapshot
    /// that differs only in `sources` must not be swallowed as "nothing moved". `fetched_ms`
    /// is the other way round - it moves every lap and is not worth a broadcast on its own.
    #[test]
    fn a_source_switched_off_is_not_the_same_readout() {
        let base = Snapshot {
            ok: true,
            plan: "max".into(),
            fetched_ms: 1_000,
            sources: vec!["anthropic".into(), "openrouter".into()],
            windows: vec![Window {
                key: "five_hour".into(),
                label: "5h".into(),
                pct: 40.0,
                unit: Unit::Pct,
                amount: None,
                limit: None,
                resets_in: None,
            }],
            ..Default::default()
        };

        let mut aged = base.clone();
        aged.fetched_ms = 99_000;
        assert!(base.same_readout(&aged), "a newer poll of the same figures");

        let mut dropped = base.clone();
        dropped.sources = vec!["anthropic".into()];
        assert!(
            !base.same_readout(&dropped),
            "openrouter was switched off and the readout must be told"
        );
    }

    /// One source failing dims the readout and says which, and never empties the other's
    /// numbers. A source switched off contributes nothing and is not a failure.
    #[test]
    fn one_source_down_keeps_the_others_rows() {
        let a = parse(&serde_json::from_str(REAL).unwrap(), "max".into());
        let c = Snapshot::failed(&Snapshot::default(), "OpenRouter rejected the key (401)");

        let m = merge([
            ("anthropic", &a),
            ("openrouter", &c),
            ("openai", &Snapshot::default()),
        ]);
        assert!(!m.ok);
        assert_eq!(m.windows.len(), a.windows.len());
        assert_eq!(m.failed_sources, vec!["openrouter"]);
        assert!(m.error.unwrap().contains("OpenRouter"));

        // Off on both sides is the snapshot the readout draws nothing from.
        assert_eq!(
            merge([
                ("anthropic", &Snapshot::default()),
                ("openrouter", &Snapshot::default()),
                ("openai", &Snapshot::default())
            ]),
            Snapshot::default()
        );
        // And one of them off leaves the other exactly as it was.
        let only = merge([
            ("anthropic", &a),
            ("openrouter", &Snapshot::default()),
            ("openai", &Snapshot::default()),
        ]);
        assert!(only.ok);
        assert_eq!(only.windows, a.windows);
    }

    /// A readout that empties itself every time the wifi hiccups is worse than a stale
    /// one.
    #[test]
    fn failure_keeps_the_last_good_numbers() {
        let good = parse(
            &serde_json::from_str(r#"{"five_hour":{"utilization":50}}"#).unwrap(),
            "max".into(),
        );
        let bad = Snapshot::failed(&good, "network unreachable");

        assert!(!bad.ok);
        assert_eq!(bad.windows, good.windows);
        assert_eq!(bad.plan, "max");
        assert_eq!(bad.error.unwrap(), "network unreachable");
    }

    #[test]
    fn parser_failure_keeps_the_last_good_numbers() {
        let good = parse(
            &serde_json::from_str(r#"{"five_hour":{"utilization":50}}"#).unwrap(),
            "max".into(),
        );
        let parsed = parse(&serde_json::json!({"changed": true}), "pro".into());
        let kept = preserve_parse_failure(&good, parsed);

        assert!(!kept.ok);
        assert_eq!(kept.windows, good.windows);
        assert_eq!(kept.plan, "max");
        assert!(kept
            .error
            .as_deref()
            .is_some_and(|error| error.contains("shape slopd does not know")));
    }

    #[test]
    fn openai_primary_and_secondary_windows_stay_separate_from_anthropic() {
        let v: Value = serde_json::from_str(
            r#"{"plan_type":"pro","rate_limit":{"primary_window":{"used_percent":12.5,"limit_window_seconds":18000,"reset_at":4102444800},"secondary_window":{"used_percent":42,"limit_window_seconds":604800,"reset_at":4102448400}}}"#,
        )
        .unwrap();

        let s = parse_openai(&v);
        assert!(s.ok);
        assert_eq!(s.plan, "pro");
        assert_eq!(s.windows[0].key, "openai_session");
        assert_eq!(s.windows[0].label, "session");
        assert_eq!(s.windows[0].pct, 12.5);
        assert_eq!(s.windows[1].key, "openai_week");
        assert_eq!(s.windows[1].label, "weekly");
        assert_eq!(s.windows[1].pct, 42.0);
        assert!(s.windows.iter().all(|w| w.resets_in.is_some()));
    }

    #[test]
    fn openai_primary_weekly_window_is_not_called_session() {
        let v: Value = serde_json::from_str(
            r#"{"plan_type":"free","rate_limit":{"primary_window":{"used_percent":12.5,"limit_window_seconds":604800,"reset_at":4102444800}}}"#,
        )
        .unwrap();

        let s = parse_openai(&v);
        assert!(s.ok);
        assert_eq!(s.windows.len(), 1);
        assert_eq!(s.windows[0].key, "openai_week");
        assert_eq!(s.windows[0].label, "weekly");
    }

    #[test]
    fn unknown_openai_payload_is_not_an_empty_usage_window() {
        let s = parse_openai(&serde_json::json!({"rate_limit": {}}));
        assert!(!s.ok);
        assert!(s.windows.is_empty());
    }
}
