use super::*;
use serde_json::json;
use std::io::{Read, Write};
use std::net::TcpListener;

// One request per server. A cache hit must succeed even after this listener closes.
fn server(status: u16, headers: &str, body: &str) -> (String, std::thread::JoinHandle<String>) {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    listener.set_nonblocking(true).unwrap();
    let url = format!("http://{}/usage", listener.local_addr().unwrap());
    let response = format!(
        "HTTP/1.1 {status} Test\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n{headers}\r\n{body}",
        body.len()
    );
    let handle = std::thread::spawn(move || {
        let deadline = Instant::now() + Duration::from_secs(5);
        let mut stream = loop {
            match listener.accept() {
                Ok((stream, _)) => break stream,
                Err(error) if error.kind() == std::io::ErrorKind::WouldBlock => {
                    assert!(
                        Instant::now() < deadline,
                        "provider did not contact local server"
                    );
                    std::thread::sleep(Duration::from_millis(5));
                }
                Err(error) => panic!("accept: {error}"),
            }
        };
        stream
            .set_read_timeout(Some(Duration::from_secs(5)))
            .unwrap();
        stream
            .set_write_timeout(Some(Duration::from_secs(5)))
            .unwrap();
        let mut request = Vec::new();
        while !request.ends_with(b"\r\n\r\n") {
            let mut byte = [0];
            stream.read_exact(&mut byte).unwrap();
            request.push(byte[0]);
            assert!(request.len() < 16 * 1024);
        }
        stream.write_all(response.as_bytes()).unwrap();
        String::from_utf8(request).unwrap()
    });
    (url, handle)
}

fn credentials(path: &Path) -> Creds {
    Creds {
        token: "test-anthropic-token".into(),
        plan: "max".into(),
        path: path.into(),
    }
}

fn header(request: &str, name: &str) -> Option<String> {
    request
        .lines()
        .filter_map(|line| line.split_once(':'))
        .find_map(|(key, value)| {
            key.eq_ignore_ascii_case(name)
                .then(|| value.trim().to_string())
        })
}

#[test]
fn openai_credentials_require_access_token_and_accept_optional_account() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let path = root.join("auth.json");
    assert!(read_openai_creds(&path).is_err());
    for value in [
        json!({}),
        json!({"tokens":{"access_token":""}}),
        json!({"tokens":{"access_token":123}}),
    ] {
        std::fs::write(&path, value.to_string()).unwrap();
        assert!(read_openai_creds(&path)
            .err()
            .unwrap()
            .to_string()
            .contains("no ChatGPT access token"));
    }
    std::fs::write(&path, "not json").unwrap();
    assert!(read_openai_creds(&path).is_err());
    for account in [None, Some(""), Some("account-123")] {
        let value = json!({"tokens":{"access_token":"access-only","account_id":account,"refresh_token":"do-not-use"}});
        std::fs::write(&path, value.to_string()).unwrap();
        let creds = read_openai_creds(&path).unwrap();
        assert_eq!(creds.token, "access-only");
        assert_eq!(
            creds.account_id.as_deref(),
            account.filter(|s| !s.is_empty())
        );
        assert_eq!(std::fs::read_to_string(&path).unwrap(), value.to_string());
    }
}

#[test]
fn openrouter_keys_are_trimmed_reread_and_file_selection_overrides_environment() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    assert!(read_key("").is_err());
    std::env::set_var(KEY_ENV, " \n ");
    assert!(read_key("").is_err());
    std::env::set_var(KEY_ENV, " env-token \n");
    assert_eq!(read_key(" \t ").unwrap(), "env-token");
    let path = root.join("key");
    let name = path.to_str().unwrap();
    assert!(read_key(name).is_err());
    std::fs::write(&path, "  ").unwrap();
    assert!(read_key(name)
        .unwrap_err()
        .to_string()
        .contains("holds no key"));
    for token in ["first", "rotated"] {
        std::fs::write(&path, format!(" {token}\n")).unwrap();
        assert_eq!(read_key(name).unwrap(), token);
    }
}

#[test]
fn credential_readers_select_the_configured_provider_and_path() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let anthropic = root.join("claude.json");
    let openai = root.join("openai.json");
    let key = root.join("key");
    std::fs::write(
        &anthropic,
        json!({"claudeAiOauth":{"accessToken":"anthropic", "subscriptionType":"max"}}).to_string(),
    )
    .unwrap();
    std::fs::write(
        &openai,
        json!({"tokens":{"access_token":"openai"}}).to_string(),
    )
    .unwrap();
    std::fs::write(&key, "router").unwrap();
    let config = crate::config::Daemon {
        claude_credentials: anthropic.to_str().unwrap().into(),
        openai_credentials: openai.to_str().unwrap().into(),
        openrouter_key_file: key.to_str().unwrap().into(),
        ..Default::default()
    };
    assert!(
        matches!(read_anthropic(&config).unwrap(), ProviderCredentials::Anthropic(c) if c.token == "anthropic" && c.plan == "max" && c.path == anthropic)
    );
    assert!(
        matches!(read_openai(&config).unwrap(), ProviderCredentials::OpenAi(c) if c.token == "openai")
    );
    assert!(
        matches!(read_openrouter(&config).unwrap(), ProviderCredentials::OpenRouter(k) if k == "router")
    );
}

#[test]
fn provider_requests_send_expected_headers_and_decode_json() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let body = json!({"test": [1, 2, 3]});
    let (url, handle) = server(200, "", &body.to_string());
    std::env::set_var("SLOPD_USAGE_URL", url);
    assert_eq!(
        fetch(&credentials(&root.join("creds"))).unwrap_or_else(|e| panic!("{e}")),
        body
    );
    let request = handle.join().unwrap();
    assert!(request.starts_with("GET /usage HTTP/1.1\r\n"));
    assert_eq!(
        header(&request, "authorization").as_deref(),
        Some("Bearer test-anthropic-token")
    );
    assert_eq!(
        header(&request, "anthropic-beta").as_deref(),
        Some(OAUTH_BETA)
    );
    assert_eq!(
        header(&request, "user-agent").as_deref(),
        Some(ANTHROPIC_USER_AGENT)
    );
    assert_eq!(
        header(&request, "accept").as_deref(),
        Some("application/json")
    );

    let (url, handle) = server(200, "", &body.to_string());
    std::env::set_var("SLOPD_CREDITS_URL", url);
    let response = fetch_openrouter(ProviderCredentials::OpenRouter("router-token".into()))
        .unwrap_or_else(|e| panic!("{e}"));
    assert_eq!(response.body, body);
    assert!(response.plan.is_none());
    let request = handle.join().unwrap();
    assert_eq!(
        header(&request, "authorization").as_deref(),
        Some("Bearer router-token")
    );
    assert!(header(&request, "anthropic-beta").is_none());

    for account in [None, Some("account-123".to_string())] {
        let (url, handle) = server(200, "", &body.to_string());
        std::env::set_var("SLOPD_OPENAI_USAGE_URL", url);
        let creds = OpenAiCreds {
            token: "openai-token".into(),
            account_id: account.clone(),
        };
        let response = fetch_openai_provider(ProviderCredentials::OpenAi(creds))
            .unwrap_or_else(|e| panic!("{e}"));
        assert_eq!(response.body, body);
        assert!(response.plan.is_none());
        let request = handle.join().unwrap();
        assert_eq!(
            header(&request, "authorization").as_deref(),
            Some("Bearer openai-token")
        );
        assert_eq!(header(&request, "ChatGPT-Account-Id"), account);
        assert!(header(&request, "anthropic-beta").is_none());
    }
}

#[test]
fn provider_http_errors_preserve_rate_limits_and_do_not_expose_credentials() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    for provider in ["anthropic", "openrouter", "openai"] {
        for (status, headers, expected_delay) in [
            (401, "", None),
            (403, "", None),
            (500, "", None),
            (429, "", Some(300)),
            (429, "Retry-After: 1\r\n", Some(300)),
            (429, "Retry-After: 900\r\n", Some(900)),
            (429, "Retry-After: invalid\r\n", Some(300)),
        ] {
            let (url, handle) = server(status, headers, "secret upstream error body");
            let result = match provider {
                "anthropic" => {
                    std::env::set_var("SLOPD_USAGE_URL", url);
                    fetch(&credentials(&root.join("creds")))
                }
                "openrouter" => {
                    std::env::set_var("SLOPD_CREDITS_URL", url);
                    fetch_credits("router-token")
                }
                _ => {
                    std::env::set_var("SLOPD_OPENAI_USAGE_URL", url);
                    fetch_openai(&OpenAiCreds {
                        token: "openai-token".into(),
                        account_id: None,
                    })
                }
            };
            let error = result.expect_err("HTTP failure");
            assert_eq!(error.retry_after, expected_delay, "{provider}: {status}");
            assert!(error.msg.contains(&status.to_string()), "{}", error.msg);
            for secret in [
                "test-anthropic-token",
                "router-token",
                "openai-token",
                "secret upstream",
            ] {
                assert!(!error.to_string().contains(secret));
            }
            handle.join().unwrap();
        }
    }
}

#[test]
fn successful_http_status_with_invalid_json_is_a_non_rate_limit_error() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    for provider in ["anthropic", "openrouter", "openai"] {
        let (url, handle) = server(200, "", "not json");
        let result = match provider {
            "anthropic" => {
                std::env::set_var("SLOPD_USAGE_URL", url);
                fetch(&credentials(&root.join("creds")))
            }
            "openrouter" => {
                std::env::set_var("SLOPD_CREDITS_URL", url);
                fetch_credits("router-token")
            }
            _ => {
                std::env::set_var("SLOPD_OPENAI_USAGE_URL", url);
                fetch_openai(&OpenAiCreds {
                    token: "openai-token".into(),
                    account_id: None,
                })
            }
        };
        assert!(result.expect_err("invalid JSON").retry_after.is_none());
        handle.join().unwrap();
    }
}

#[test]
fn anthropic_fetch_reuses_cached_success_and_shared_backoff() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let creds_path = root.join("creds");
    let body = json!({"five_hour":{"utilization":42}});
    let (url, handle) = server(200, "", &body.to_string());
    std::env::set_var("SLOPD_USAGE_URL", url);
    let fetch_cached = || fetch_anthropic(ProviderCredentials::Anthropic(credentials(&creds_path)));
    let first = fetch_cached().unwrap_or_else(|e| panic!("{e}"));
    handle.join().unwrap();
    assert_eq!(first.body, body);
    assert_eq!(first.plan.as_deref(), Some("max"));
    let second = fetch_cached().unwrap_or_else(|e| panic!("cache miss: {e}"));
    assert_eq!(second.body, body);
    let (cache, _) = anthropic_cache_paths(&creds_path);
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
    std::env::set_var("SLOPD_USAGE_URL", url);
    assert_ne!(anthropic_cache_paths(&creds_path).0, cache);
    assert_eq!(fetch_cached().err().unwrap().retry_after, Some(900));
    handle.join().unwrap();
    let error = fetch_cached().err().expect("shared backoff");
    assert!(error.msg.contains("shared rate-limit backoff"));
    assert!(matches!(error.retry_after, Some(895..=900)));
    assert!(fresh_anthropic_cache(&anthropic_cache_paths(&creds_path).0).is_none());
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
        (2, super::super::now_ms(), json!({"test":1})),
        (1, super::super::now_ms() + 60_000, json!({"test":1})),
        (1, super::super::now_ms() - 600_000, json!({"test":1})),
        (1, super::super::now_ms(), Value::Null),
    ] {
        let cache = AnthropicUsageCache {
            version,
            fetched_ms,
            body,
            retry_until_ms: Some(super::super::now_ms() - 1000),
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
    std::env::set_var("SLOPD_USAGE_URL", url);
    assert_ne!(anthropic_cache_paths(&one), anthropic_cache_paths(&two));
    let (cache, lock) = anthropic_cache_paths(&one);
    std::fs::create_dir(&lock).unwrap();
    let response = fetch_anthropic(ProviderCredentials::Anthropic(credentials(&one)))
        .unwrap_or_else(|e| panic!("{e}"));
    assert_eq!(response.body, json!({"ok":true}));
    handle.join().unwrap();
    assert!(!cache.exists());
}

#[test]
fn mismatched_credentials_are_rejected_before_any_network_request() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    for fetcher in [fetch_anthropic as FetchProvider, fetch_openai_provider] {
        let error = fetcher(ProviderCredentials::OpenRouter("test".into()))
            .err()
            .unwrap();
        assert!(error.msg.contains("credential mismatch"));
        assert!(error.retry_after.is_none());
    }
    let error = fetch_openrouter(ProviderCredentials::OpenAi(OpenAiCreds {
        token: "test".into(),
        account_id: None,
    }))
    .err()
    .unwrap();
    assert!(error.msg.contains("credential mismatch"));
}
