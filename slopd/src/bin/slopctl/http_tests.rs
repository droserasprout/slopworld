use super::*;
use crate::shared::application_paths::Mode;
use crate::tests::http_fixture::{serve, serve_raw, serve_sequence, serve_truncated};

#[test]
fn endpoint_credentials_follow_overrides_and_report_descriptor_failures() {
    const CASE: &str = "SLOPCTL_ENDPOINT_TEST_CASE";
    if let Ok(case) = std::env::var(CASE) {
        match case.as_str() {
            "paired" => {
                let endpoint = load_endpoint(Mode::Native).unwrap();
                assert_eq!(endpoint.url, "http://override");
                assert_eq!(endpoint.token, "override-token");
            }
            "unpaired" => {
                let error = load_endpoint(Mode::Native).err().unwrap();
                assert!(error.contains("SLOPD_TOKEN is missing"), "{error}");
            }
            "native" | "sidecar" => {
                let mode = if case == "native" {
                    Mode::Native
                } else {
                    Mode::Sidecar
                };
                let endpoint = load_endpoint(mode).unwrap();
                assert_eq!(endpoint.url, format!("http://{case}"));
                assert_eq!(endpoint.token, format!("{case}-token"));
            }
            "explicit" => {
                let endpoint = load_endpoint(Mode::Sidecar).unwrap();
                assert_eq!(endpoint.url, "http://native");
                assert_eq!(endpoint.token, "native-token");
            }
            "missing" | "invalid" => {
                let error = load_endpoint(Mode::Native).err().unwrap();
                let verb = if case == "missing" {
                    "reading"
                } else {
                    "parsing"
                };
                assert!(error.starts_with(verb), "{error}");
                assert!(error.contains(&format!("{case}.toml")), "{error}");
            }
            _ => panic!("unknown endpoint fixture"),
        }
        return;
    }
    let root = std::env::temp_dir().join(format!("slopctl-endpoint-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(root.join("sidecar")).unwrap();
    for (path, name) in [
        ("endpoint.toml", "native"),
        ("sidecar/endpoint.toml", "sidecar"),
    ] {
        std::fs::write(
            root.join(path),
            format!("url = 'http://{name}'\ntoken = '{name}-token'\n"),
        )
        .unwrap();
    }
    std::fs::write(root.join("invalid.toml"), "not valid TOML").unwrap();
    let outcomes = [
        "paired", "unpaired", "native", "sidecar", "explicit", "missing", "invalid",
    ]
    .map(|case| {
        let mut command = std::process::Command::new(std::env::current_exe().unwrap());
        command
            .args([
                "--exact",
                "http::tests::endpoint_credentials_follow_overrides_and_report_descriptor_failures",
                "--nocapture",
            ])
            .env(CASE, case)
            .env("SLOPD_CONFIG_ROOT", &root)
            .env_remove("SLOPD_URL")
            .env_remove("SLOPD_TOKEN")
            .env_remove("SLOPD_ENDPOINT");
        match case {
            "paired" | "unpaired" => {
                command.env("SLOPD_URL", "http://override");
                if case == "paired" {
                    command.env("SLOPD_TOKEN", "override-token");
                }
            }
            "explicit" => {
                command.env("SLOPD_ENDPOINT", root.join("endpoint.toml"));
            }
            "missing" | "invalid" => {
                command.env("SLOPD_ENDPOINT", root.join(format!("{case}.toml")));
            }
            _ => {}
        }
        (case, command.output())
    });
    std::fs::remove_dir_all(root).unwrap();
    for (case, output) in outcomes {
        let output = output.unwrap();
        assert!(
            output.status.success(),
            "{case}: {}\n{}",
            String::from_utf8_lossy(&output.stdout),
            String::from_utf8_lossy(&output.stderr)
        );
    }
}

#[test]
fn malformed_wire_and_truncated_bodies_report_response_errors() {
    for (status, expected) in [
        ("200 OK", "response 200"),
        ("403 Forbidden", "refused: 403"),
    ] {
        let (endpoint, server) = serve_raw(status, http_wire::CONTENT_TYPE, &[0x80]);
        let error = request(&endpoint, "host", "GET", "/api/health", None).unwrap_err();
        server.join().unwrap().unwrap();
        assert!(error.starts_with(expected), "{error}");
    }
    let (endpoint, server) = serve_truncated();
    let error = request(&endpoint, "host", "GET", "/api/health", None).unwrap_err();
    server.join().unwrap().unwrap();
    assert!(error.starts_with("response:"), "{error}");
}

#[test]
fn response_limit_accepts_the_boundary_and_rejects_one_more_byte() {
    for (size, oversized) in [(32 * 1024 * 1024, false), (32 * 1024 * 1024 + 1, true)] {
        // Zero is an invalid Protobuf tag: at the boundary decoding must still
        // run, while the extra byte must be rejected before decoding.
        let (endpoint, server) = serve_raw("200 OK", http_wire::CONTENT_TYPE, &vec![0; size]);
        let error = request(&endpoint, "host", "GET", "/api/health", None).unwrap_err();
        server.join().unwrap().unwrap();
        if oversized {
            assert_eq!(error, "response exceeds limit");
        } else {
            assert!(error.starts_with("response 200"), "{error}");
        }
    }
}

#[test]
fn unsupported_requests_and_invalid_payloads_fail_before_connecting() {
    let endpoint = Endpoint {
        url: "http://127.0.0.1:0".into(),
        token: "unused".into(),
    };
    for (method, body) in [
        ("PATCH", None),
        ("POST", None),
        ("PUT", None),
        ("GET", Some(json!({}))),
        ("DELETE", Some(json!({}))),
    ] {
        assert_eq!(
            request(&endpoint, "host", method, "/api/tasks", body).unwrap_err(),
            format!("unsupported request: {method}")
        );
    }
    let error = request(
        &endpoint,
        "host",
        "POST",
        "/api/tasks",
        Some(json!({"unknown_field": true})),
    )
    .unwrap_err();
    assert!(!error.starts_with("request:"), "{error}");
}

#[test]
fn status_counts_only_open_tasks_in_each_direction_and_omits_credentials() {
    let tasks = json!({"tasks": [
        {"from":"peer", "to":"host", "status":"pending"},
        {"from":"host", "to":"peer", "status":"accepted"},
        {"from":"host", "to":"host", "status":"working"},
        {"from":"peer", "to":"elsewhere", "status":"pending"},
        {"from":"host", "to":"host", "status":"done"},
        {"from":"host", "to":"host", "status":"failed"},
        {"from":"host", "to":"host", "status":"canceled"}
    ]});
    let (mut endpoint, server) = serve_sequence(vec![
        (
            "200 OK",
            json!({"ok":true,"version":"test-version"}).to_string(),
        ),
        ("200 OK", tasks.to_string()),
    ]);
    endpoint.url.push('/');
    let value = status_value(&endpoint, "host");
    let requests = server.join().unwrap().unwrap();
    assert_eq!(
        value,
        json!({
            "session":"host", "endpoint":endpoint.url, "daemon":"ok",
            "version":"test-version", "waiting":2, "sent":2
        })
    );
    assert!(requests.starts_with("GET /api/health HTTP/1.1\r\n"));
    assert!(requests.contains("GET /api/tasks HTTP/1.1\r\n"));
    assert_eq!(
        requests
            .to_ascii_lowercase()
            .matches("x-slop-token: secret\r\n")
            .count(),
        2
    );
    assert_eq!(
        requests
            .to_ascii_lowercase()
            .matches("x-slop-session: host\r\n")
            .count(),
        2
    );
}

#[test]
fn status_keeps_health_when_tasks_fail_and_stops_when_health_fails() {
    let (endpoint, server) = serve_sequence(vec![
        (
            "200 OK",
            json!({"ok":true,"version":"test-version"}).to_string(),
        ),
        ("403 Forbidden", json!({"error":"tasks denied"}).to_string()),
    ]);
    let value = status_value(&endpoint, "host");
    server.join().unwrap().unwrap();
    assert_eq!(value["daemon"], "ok");
    assert_eq!(value["version"], "test-version");
    assert_eq!(value["error"], "tasks denied");
    assert!(value.get("waiting").is_none());
    assert!(value.get("sent").is_none());

    let (endpoint, server) = serve("503 Service Unavailable", r#"{"error":"maintenance"}"#);
    let value = status_value(&endpoint, "host");
    let requests = server.join().unwrap().unwrap();
    assert_eq!(value["daemon"], "unreachable");
    assert_eq!(value["error"], "maintenance");
    assert!(value.get("version").is_none());
    assert!(!requests.contains("/api/tasks"));
}

#[test]
fn status_reports_transport_failure_without_exposing_the_token() {
    let endpoint = Endpoint {
        url: "not-an-http-url".into(),
        token: "private-token".into(),
    };
    let value = status_value(&endpoint, "host");
    assert_eq!(value["daemon"], "unreachable");
    assert!(value["error"].as_str().unwrap().starts_with("request:"));
    assert!(!value.to_string().contains("private-token"));
    assert!(value.get("waiting").is_none());
}
