//! Shared Anthropic cache identity, bounded locking, and request deduplication.
//! providers owns credentials and HTTP; polling owns per-daemon retry schedules.

use super::providers::{self, usage_url, Creds, PollErr, ProviderResponse, RATE_LIMIT_FLOOR};
use crate::clock::unix_ms;
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::fs::{File, OpenOptions};
use std::path::{Path, PathBuf};
use std::time::{Duration, Instant};

const USAGE_CACHE_TTL: Duration = Duration::from_secs(300);
const USAGE_CACHE_VERSION: u32 = 1;
const CACHE_LOCK_TIMEOUT: Duration = Duration::from_secs(1);

#[derive(Debug, Deserialize, Serialize)]
struct AnthropicUsageCache {
    version: u32,
    fetched_ms: u64,
    body: Value,
    /// Set only after a 429, so another daemon can observe the same upstream backoff.
    #[serde(default)]
    retry_until_ms: Option<u64>,
}

/// Cache and lock files live under XDG's cache root, partitioned by credentials and endpoint.
fn anthropic_cache_paths(creds: &Creds) -> (PathBuf, PathBuf) {
    let mut hash = 0xcbf29ce484222325u64;
    for byte in creds
        .path
        .to_string_lossy()
        .bytes()
        .chain([0u8])
        .chain(usage_url().bytes())
    {
        hash = (hash ^ u64::from(byte)).wrapping_mul(0x100000001b3);
    }

    // Partition by access-token identity as well as path and endpoint. Rotation may
    // conservatively miss the cache, but an account switch cannot reuse data or backoff.
    // Only the SHA-256 digest enters the filename; credentials never enter cache bodies.
    let identity = ring::digest::digest(&ring::digest::SHA256, creds.token.as_bytes());
    let identity: String = identity
        .as_ref()
        .iter()
        .map(|byte| format!("{byte:02x}"))
        .collect();
    let base = crate::paths::cache_root().join(format!(".anthropic-usage-{hash:016x}-{identity}"));
    (base.with_extension("json"), base.with_extension("lock"))
}

impl AnthropicUsageCache {
    /// Validate the format before either successful data or shared retry state is used.
    fn read(path: &Path) -> Option<Self> {
        let text = std::fs::read_to_string(path).ok()?;
        let cache = serde_json::from_str::<Self>(&text).ok()?;
        (cache.version == USAGE_CACHE_VERSION).then_some(cache)
    }

    fn fresh_body(self) -> Option<Value> {
        if self.body.is_null() {
            return None;
        }
        let age = unix_ms().checked_sub(self.fetched_ms)?;
        (age < USAGE_CACHE_TTL.as_millis() as u64).then_some(self.body)
    }

    fn retry_delay(&self) -> Option<u64> {
        let remaining_ms = self.retry_until_ms?.checked_sub(unix_ms())?;
        (remaining_ms > 0).then(|| remaining_ms.saturating_add(999) / 1000)
    }
}

#[cfg(test)]
fn fresh_anthropic_cache(path: &Path) -> Option<Value> {
    AnthropicUsageCache::read(path)?.fresh_body()
}

#[cfg(test)]
fn cached_anthropic_retry(path: &Path) -> Option<u64> {
    AnthropicUsageCache::read(path)?.retry_delay()
}

/// Bound contention so another daemon cannot stall all provider polling. The caller
/// skips the request on timeout; other filesystem failures allow an uncached request.
fn lock_anthropic_cache(path: &Path) -> std::io::Result<nix::fcntl::Flock<File>> {
    lock_with_timeout(path, CACHE_LOCK_TIMEOUT)
}

fn lock_with_timeout(path: &Path, timeout: Duration) -> std::io::Result<nix::fcntl::Flock<File>> {
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent)?;
    }
    let mut file = OpenOptions::new()
        .create(true)
        .truncate(false)
        .read(true)
        .write(true)
        .open(path)?;
    let deadline = Instant::now() + timeout;
    loop {
        match nix::fcntl::Flock::lock(file, nix::fcntl::FlockArg::LockExclusiveNonblock) {
            Ok(lock) => return Ok(lock),
            Err((returned, error)) => {
                file = returned;
                if error != nix::errno::Errno::EWOULDBLOCK {
                    return Err(std::io::Error::from_raw_os_error(error as i32));
                }
                let remaining = deadline.saturating_duration_since(Instant::now());
                if remaining.is_zero() {
                    return Err(std::io::Error::new(
                        std::io::ErrorKind::TimedOut,
                        "Anthropic usage cache is busy",
                    ));
                }
                std::thread::sleep(remaining.min(Duration::from_millis(10)));
            }
        }
    }
}

fn save_anthropic_cache(path: &Path, body: &Value, retry_until_ms: Option<u64>) {
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
        drop(std::fs::set_permissions(
            &tmp,
            std::fs::Permissions::from_mode(0o600),
        ));
    }
    drop(std::fs::rename(tmp, path));
}

fn save_anthropic_rate_limit(path: &Path, delay: u64) {
    let delay = delay.max(RATE_LIMIT_FLOOR);
    save_anthropic_cache(
        path,
        &Value::Null,
        Some(unix_ms().saturating_add(delay.saturating_mul(1_000))),
    );
}

/// Check both cache answers together, before and after acquiring the request guard.
fn cached_response(path: &Path, plan: &str) -> Option<Result<ProviderResponse, PollErr>> {
    let cache = AnthropicUsageCache::read(path)?;
    if let Some(delay) = cache.retry_delay() {
        return Some(Err(PollErr {
            msg: "Anthropic usage polling is in shared rate-limit backoff".into(),
            retry_after: Some(delay),
        }));
    }
    cache.fresh_body().map(|body| {
        Ok(ProviderResponse {
            body,
            plan: Some(plan.into()),
        })
    })
}

pub(super) fn fetch(creds: &Creds) -> Result<ProviderResponse, PollErr> {
    let (cache_path, lock_path) = anthropic_cache_paths(creds);
    if let Some(answer) = cached_response(&cache_path, &creds.plan) {
        return answer;
    }
    let lock = match lock_anthropic_cache(&lock_path) {
        Ok(lock) => Some(lock),
        Err(error) if error.kind() == std::io::ErrorKind::TimedOut => {
            // A held lock means another request may still be active. Do not duplicate it.
            return cached_response(&cache_path, &creds.plan)
                .unwrap_or_else(|| Err(PollErr::new(error)));
        }
        Err(_) => None,
    };
    if lock.is_some() {
        // Another daemon may have published while this one waited. Keep the guard
        // alive through the request and cache save, including the rate-limit path.
        if let Some(answer) = cached_response(&cache_path, &creds.plan) {
            return answer;
        }
    }
    let body = match providers::fetch(creds) {
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
        plan: Some(creds.plan.clone()),
    })
}

#[cfg(test)]
#[path = "anthropic_cache_tests.rs"]
mod tests;
