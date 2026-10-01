use super::super::providers::{ProviderCredentials, fetch_anthropic};
use super::super::test_http::{credentials, server};
use super::*;
use serde_json::json;

#[test]
fn anthropic_fetch_reuses_cached_success_and_shared_backoff() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let creds_path = root.join("creds");
    let body = json!({"five_hour":{"utilization":42}});
    let (url, handle) = server(200, "", &body.to_string());
    crate::test_support::set_env("SLOPD_USAGE_URL", url);
    let fetch_cached = || fetch_anthropic(ProviderCredentials::Anthropic(credentials(&creds_path)));
    let first = fetch_cached().unwrap_or_else(|e| panic!("{e}"));
    handle.join().unwrap();
    assert_eq!(first.body, body);
    assert_eq!(first.plan.as_deref(), Some("max"));
    let second = fetch_cached().unwrap_or_else(|e| panic!("cache miss: {e}"));
    assert_eq!(second.body, body);
    let (cache, _) = anthropic_cache_paths(&credentials(&creds_path));
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        assert_eq!(
            std::fs::metadata(&cache).unwrap().permissions().mode() & 0o777,
            0o600
        );
    }
    assert!(!cache.with_extension("json.tmp").exists());

    let (url, handle) = server(429, "Retry-After: 900\r\n", "{}");
    crate::test_support::set_env("SLOPD_USAGE_URL", url);
    assert_ne!(anthropic_cache_paths(&credentials(&creds_path)).0, cache);
    assert_eq!(fetch_cached().err().unwrap().retry_after, Some(900));
    handle.join().unwrap();
    let error = fetch_cached().err().expect("shared backoff");
    assert!(error.msg.contains("shared rate-limit backoff"));
    assert!(matches!(error.retry_after, Some(895..=900)));
    assert!(fresh_anthropic_cache(&anthropic_cache_paths(&credentials(&creds_path)).0).is_none());
}

#[test]
fn anthropic_cache_rejects_stale_future_unknown_and_corrupt_entries() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let path = root.join("cache.json");
    assert!(fresh_anthropic_cache(&path).is_none());
    assert!(cached_anthropic_retry(&path).is_none());
    std::fs::write(&path, "bad json").unwrap();
    assert!(fresh_anthropic_cache(&path).is_none());
    assert!(cached_anthropic_retry(&path).is_none());
    for (version, fetched_ms, body) in [
        (USAGE_CACHE_VERSION + 1, unix_ms(), json!({"test":1})),
        (1, unix_ms() + 60_000, json!({"test":1})),
        (1, unix_ms() - 600_000, json!({"test":1})),
        (1, unix_ms(), Value::Null),
    ] {
        let cache = AnthropicUsageCache {
            version,
            fetched_ms,
            body,
            retry_until_ms: Some(if version != USAGE_CACHE_VERSION {
                unix_ms() + 60_000
            } else {
                unix_ms() - 1000
            }),
        };
        std::fs::write(&path, serde_json::to_string(&cache).unwrap()).unwrap();
        assert!(fresh_anthropic_cache(&path).is_none());
        assert!(cached_anthropic_retry(&path).is_none());
    }
}

#[test]
fn cache_keys_separate_accounts_and_endpoints_and_lock_failure_still_fetches() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let one = root.join("one");
    let two = root.join("two");
    let (url, handle) = server(200, "", "{\"ok\":true}");
    crate::test_support::set_env("SLOPD_USAGE_URL", url);
    assert_ne!(
        anthropic_cache_paths(&credentials(&one)),
        anthropic_cache_paths(&credentials(&two))
    );
    let (cache, lock) = anthropic_cache_paths(&credentials(&one));
    std::fs::create_dir(&lock).unwrap();
    let response = fetch_anthropic(ProviderCredentials::Anthropic(credentials(&one)))
        .unwrap_or_else(|e| panic!("{e}"));
    assert_eq!(response.body, json!({"ok":true}));
    handle.join().unwrap();
    assert!(!cache.exists());
}

#[test]
fn anthropic_cache_shares_successes_and_rate_limit_backoff() {
    let path = std::env::temp_dir().join(format!(
        "slopd-anthropic-cache-{}-{}.json",
        std::process::id(),
        unix_ms()
    ));
    let body = serde_json::json!({"five_hour": {"utilization": 12}});

    save_anthropic_cache(&path, &body, None);
    assert_eq!(fresh_anthropic_cache(&path), Some(body));

    save_anthropic_rate_limit(&path, RATE_LIMIT_FLOOR_SECS);
    assert!(fresh_anthropic_cache(&path).is_none());
    assert!(cached_anthropic_retry(&path).is_some_and(|seconds| seconds > 0));

    std::fs::remove_file(path).unwrap();
}

#[test]
fn cache_lock_bootstraps_parent_and_bounds_contention() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let path = root.join("cold/cache/usage.lock");
    let held = lock_anthropic_cache(&path).unwrap();
    let contender = std::thread::spawn({
        let path = path.clone();
        move || lock_with_timeout(&path, Duration::ZERO)
    });
    assert_eq!(
        contender.join().unwrap().unwrap_err().kind(),
        std::io::ErrorKind::TimedOut
    );
    drop(held);
    lock_with_timeout(&path, Duration::ZERO).unwrap();
}

#[test]
fn contended_cache_defers_without_an_outbound_request() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let listener = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
    listener.set_nonblocking(true).unwrap();
    crate::test_support::set_env(
        "SLOPD_USAGE_URL",
        format!("http://{}/usage", listener.local_addr().unwrap()),
    );
    let creds = credentials(&root.join("creds"));
    let (_, lock_path) = anthropic_cache_paths(&creds);
    let held = lock_anthropic_cache(&lock_path).unwrap();
    let before = Instant::now();
    let error = fetch(&creds).err().expect("busy cache must defer request");
    assert!(error.msg.contains("cache is busy"));
    assert!(before.elapsed() < Duration::from_secs(5));
    assert_eq!(
        listener.accept().unwrap_err().kind(),
        std::io::ErrorKind::WouldBlock
    );
    drop(held);
}

#[test]
fn changed_credentials_at_the_same_path_cannot_reuse_success_or_backoff() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    for rate_limited in [false, true] {
        let body = json!({"five_hour":{"utilization":77}});
        let (url, handle) = server(200, "", &body.to_string());
        crate::test_support::set_env("SLOPD_USAGE_URL", url);
        let old = credentials(&root.join("same-credentials-path"));
        let (old_cache, old_lock) = anthropic_cache_paths(&old);
        if rate_limited {
            save_anthropic_rate_limit(&old_cache, 900);
        } else {
            save_anthropic_cache(&old_cache, &json!({"five_hour":{"utilization":11}}), None);
        }
        let mut changed = credentials(&old.path);
        changed.token = "different-account-access-token".into();
        let (new_cache, new_lock) = anthropic_cache_paths(&changed);
        assert_ne!(old_cache, new_cache);
        assert_ne!(old_lock, new_lock);
        assert!(!new_cache.to_string_lossy().contains(&changed.token));
        let response = fetch(&changed).unwrap_or_else(|error| panic!("{error}"));
        assert_eq!(response.body, body);
        let request = handle.join().unwrap();
        assert_eq!(
            super::super::test_http::header(&request, "authorization").as_deref(),
            Some("Bearer different-account-access-token")
        );
        assert!(
            !std::fs::read_to_string(new_cache)
                .unwrap()
                .contains(&changed.token)
        );
    }
}
