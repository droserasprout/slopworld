use super::*;
use crate::test_http::{accept_request, read_request};
use std::{io::Write, net::TcpListener, thread};

fn server(response: serde_json::Value) -> (String, thread::JoinHandle<()>) {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    listener.set_nonblocking(true).unwrap();
    let endpoint = format!("http://{}", listener.local_addr().unwrap());
    let handle = thread::spawn(move || {
        let mut stream = accept_request(&listener, Duration::from_secs(5)).unwrap();
        read_request(&stream, Duration::from_secs(3)).unwrap();
        stream
            .set_write_timeout(Some(Duration::from_secs(3)))
            .unwrap();
        let body = response.to_string();
        write!(
            stream,
            "HTTP/1.1 200 OK\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{}",
            body.len(),
            body
        )
        .unwrap();
    });
    (endpoint, handle)
}

#[test]
fn planner_completion_retains_long_multiline_content_and_rejects_truncation() {
    let content = "First step\nSecond step\n".repeat(20);
    let (endpoint, server) =
        server(json!({"choices":[{"finish_reason":"stop","message":{"content":content}}]}));
    assert_eq!(
        complete_with_key("task", "plan", "key", "model", &endpoint).unwrap(),
        content
    );
    server.join().unwrap();
    let (endpoint, server) = self::server(
        json!({"choices":[{"finish_reason":"length","message":{"content":"Partial work order"}}]}),
    );
    assert!(
        complete_with_key("task", "execute", "key", "model", &endpoint)
            .unwrap_err()
            .to_string()
            .contains("truncated")
    );
    server.join().unwrap();
}

#[test]
fn planner_completion_rejects_empty_provider_content() {
    let (endpoint, server) = server(json!({"choices":[{"message":{"content":" "}}]}));
    complete_with_key("task", "plan", "key", "model", &endpoint).unwrap_err();
    server.join().unwrap();
}
