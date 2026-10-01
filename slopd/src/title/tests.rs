use super::{clean, message_content, summarize_with_key};
use crate::test_http::{accept_request, read_request};
use serde_json::json;
use std::io::Write;
use std::net::TcpListener;
use std::thread;
use std::time::Duration;

fn mock_server(responses: Vec<(u16, &'static str)>) -> (String, thread::JoinHandle<usize>) {
    let listener = TcpListener::bind("127.0.0.1:0").expect("bind mock title server");
    let endpoint = format!("http://{}", listener.local_addr().unwrap());
    listener.set_nonblocking(true).unwrap();
    let handle = thread::spawn(move || {
        let mut requests = 0;
        for (status, response) in responses {
            let mut stream = match accept_request(&listener, Duration::from_secs(3)) {
                Ok(stream) => stream,
                Err(error) if error.kind() == std::io::ErrorKind::TimedOut => return requests,
                Err(error) => panic!("accept title request: {error}"),
            };
            read_request(&stream, Duration::from_secs(2)).expect("complete title request");
            stream
                .set_write_timeout(Some(Duration::from_secs(2)))
                .unwrap();
            requests += 1;
            let bytes = response.as_bytes();
            let reason = if status == 200 {
                "OK"
            } else {
                "Service Unavailable"
            };
            write!(
                stream,
                "HTTP/1.1 {status} {reason}\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{}",
                bytes.len(),
                response
            )
            .expect("write title response");
        }
        requests
    });
    (endpoint, handle)
}

#[test]
fn cleans_model_decoration_and_caps_length() {
    assert_eq!(
        clean("  \"Add Codex status titles.\"  ").as_deref(),
        Some("Add Codex status titles")
    );
    assert_eq!(clean("\"Title\".").as_deref(), Some("Title"));
    assert_eq!(clean("   "), None);
    assert_eq!(clean(&"x".repeat(80)).unwrap().chars().count(), 60);
}

#[test]
fn accepts_string_and_text_block_content() {
    assert_eq!(
        message_content(&json!({
            "choices": [{"message": {"content": "Fix parser"}}]
        }))
        .as_deref(),
        Some("Fix parser")
    );
    assert_eq!(
        message_content(&json!({
            "choices": [{"message": {"content": [
                {"type": "text", "text": "Fix "},
                {"type": "text", "text": "parser"}
            ]}}]
        }))
        .as_deref(),
        Some("Fix parser")
    );
}

#[test]
fn retries_a_transient_title_response() {
    let (endpoint, server) = mock_server(vec![
        (503, "{\"error\":{\"message\":\"busy\"}}"),
        (
            200,
            "{\"choices\":[{\"message\":{\"content\":\"Fix parser\"}}]}",
        ),
    ]);
    let title = summarize_with_key(
        "fix the parser",
        "Summarise in six words.",
        "test-key",
        "test/model",
        &endpoint,
    );
    assert_eq!(title.unwrap(), "Fix parser");
    assert_eq!(server.join().unwrap(), 2);
}

#[test]
fn permanent_provider_error_is_bounded_filtered_and_not_retried() {
    let (endpoint, server) = mock_server(vec![(
        400,
        r#"{"error":{"message":"Invalid model test-key\u001b\n"}}"#,
    )]);
    let error = summarize_with_key("prompt", "", "test-key", "missing", &endpoint)
        .unwrap_err()
        .to_string();
    assert!(error.contains("400"));
    assert!(error.contains("Invalid model [redacted]"));
    assert!(!error.contains("test-key"));
    assert!(!error.chars().any(char::is_control));
    assert_eq!(server.join().unwrap(), 1);
}
