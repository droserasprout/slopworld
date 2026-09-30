use super::{Endpoint, update_token, url_for, write_endpoint};

#[test]
fn loopback_url_is_written_for_wildcard_binds() {
    assert_eq!(url_for("0.0.0.0:7717"), "http://127.0.0.1:7717");
    assert_eq!(url_for("[::]:7717"), "http://[::1]:7717");
}

#[test]
fn ipv6_urls_are_bracketed() {
    assert_eq!(url_for("[::1]:7717"), "http://[::1]:7717");
    assert_eq!(url_for("127.0.0.1:9000"), "http://127.0.0.1:9000");
    assert_eq!(url_for("localhost:7717"), "http://localhost:7717");
}

#[test]
fn descriptor_round_trips_through_toml() {
    let endpoint = Endpoint {
        url: "http://127.0.0.1:7717".into(),
        token: "quotes \" and slash \\".into(),
    };
    let text = toml::to_string_pretty(&endpoint).unwrap();
    let back: Endpoint = toml::from_str(&text).unwrap();
    assert_eq!(back.url, endpoint.url);
    assert_eq!(back.token, endpoint.token);
}

#[tokio::test]
async fn descriptor_writes_and_token_updates_preserve_the_bound_url() {
    let path = std::env::temp_dir().join(format!(
        "slopd-endpoint-{}-{}.toml",
        std::process::id(),
        uuid::Uuid::new_v4()
    ));
    let endpoint = Endpoint {
        url: "http://127.0.0.1:7717".into(),
        token: "old".into(),
    };
    write_endpoint(&path, &endpoint).await.unwrap();

    let initial: Endpoint = toml::from_str(&std::fs::read_to_string(&path).unwrap()).unwrap();
    assert_eq!(initial.url, endpoint.url);
    assert_eq!(initial.token, endpoint.token);

    update_token(&path, "new").await.unwrap();

    let written: Endpoint = toml::from_str(&std::fs::read_to_string(&path).unwrap()).unwrap();
    assert_eq!(written.url, endpoint.url);
    assert_eq!(written.token, "new");
    std::fs::remove_file(path).unwrap();
}

#[test]
fn debug_redacts_credentials() {
    let endpoint = Endpoint {
        url: "http://localhost:7717".into(),
        token: "secret-token".into(),
    };
    let debug = format!("{endpoint:?}");
    assert!(debug.contains(&endpoint.url));
    assert!(debug.contains("[redacted]"));
    assert!(!debug.contains(&endpoint.token));
}
