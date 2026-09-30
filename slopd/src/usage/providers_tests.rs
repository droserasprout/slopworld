use super::super::test_http::{credentials, header, server};
use super::*;
use serde_json::json;
use std::net::TcpListener;
use std::time::UNIX_EPOCH;

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
    read_key("").unwrap_err();
    std::env::set_var(KEY_ENV, " \n ");
    read_key("").unwrap_err();
    std::env::set_var(KEY_ENV, " env-token \n");
    assert_eq!(read_key(" \t ").unwrap(), "env-token");
    let path = root.join("key");
    let name = path.to_str().unwrap();
    read_key(name).unwrap_err();
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
fn mismatched_credentials_are_rejected_before_any_network_request() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    listener.set_nonblocking(true).unwrap();
    let url = format!("http://{}/usage", listener.local_addr().unwrap());
    for variable in [
        "SLOPD_USAGE_URL",
        "SLOPD_CREDITS_URL",
        "SLOPD_OPENAI_USAGE_URL",
    ] {
        std::env::set_var(variable, &url);
    }
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
    assert_eq!(
        listener.accept().unwrap_err().kind(),
        std::io::ErrorKind::WouldBlock
    );
}
/// Credential writes can truncate the existing file when a sandbox bind mount prevents atomic replacement.
/// A concurrent read can then encounter incomplete JSON.
/// Retry a parse failure once to avoid reporting a temporary write as a credential error.
#[test]
fn a_half_written_credentials_file_is_read_again_rather_than_failed() {
    let path = std::env::temp_dir().join(format!("slopd-torn-{}.json", std::process::id()));
    let whole = r#"{"claudeAiOauth":{"accessToken":"t","subscriptionType":"max"}}"#;

    // Simulate an empty file after O_TRUNC and before the writer supplies new contents.
    std::fs::write(&path, "").unwrap();
    let (release_writer, failed_read) = std::sync::mpsc::channel();
    let (write_done, await_write) = std::sync::mpsc::channel();
    let writer = {
        let path = path.clone();
        std::thread::spawn(move || {
            failed_read.recv().unwrap();
            std::fs::write(&path, whole).unwrap();
            write_done.send(()).unwrap();
        })
    };
    let got = read_creds_with_retry(&path, move || {
        release_writer.send(()).unwrap();
        await_write.recv().unwrap();
    });
    writer.join().unwrap();
    assert_eq!(
        got.unwrap().token,
        "t",
        "the retry did not pick up the write"
    );

    // Report a parse error if the retry also reads invalid JSON.
    std::fs::write(&path, "half a {").unwrap();
    assert!(read_creds(&path).is_err());

    // Report a missing file as an I/O error without a retry.
    std::fs::remove_file(&path).unwrap();
    assert!(read_creds(&path).is_err());
}

/// Compare realistic epoch milliseconds to expose incorrect unit conversions.
#[test]
fn expiry_reads_milliseconds() {
    // Issued 15:10 UTC-3, eight hours to run.
    let exp = 1_786_327_807_534;
    let refresh = 1_788_638_739_534;

    assert!(expiry_error(Some(exp), Some(refresh), exp - 1).is_none());
    assert!(expiry_error(Some(exp), Some(refresh), exp + 1).is_some());

    // Incorrect conversion of either timestamp changes the expiry result.
    // These comparisons show why callers must supply consistent units.
    assert!(expiry_error(Some(exp), Some(refresh), exp / 1000).is_none());
    assert!(expiry_error(Some(exp * 1000), Some(refresh), exp + 10_800_000).is_none());
}

/// Do not request `claude auth` when the host can renew the access token.
#[test]
fn a_renewable_token_is_not_a_logged_out_host() {
    let now = 1_786_327_807_534;
    let stale = now - 1;
    let live_refresh = now + 30 * 86_400_000;

    let renew = expiry_error(Some(stale), Some(live_refresh), now).unwrap();
    assert!(renew.contains("needs renewal"));
    assert!(!renew.contains("claude auth"));

    let relogin = expiry_error(Some(stale), Some(stale), now).unwrap();
    assert!(relogin.contains("claude auth"));

    // An absent refresh expiry does not prove that the refresh token has expired.
    assert!(!expiry_error(Some(stale), None, now)
        .unwrap()
        .contains("claude auth"));

    // An absent access-token expiry does not prove that the access token has expired.
    assert!(expiry_error(None, Some(stale), now).is_none());
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
