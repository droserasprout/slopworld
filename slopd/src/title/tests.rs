use super::{clean, message_content, summarize_with_key};
use serde_json::json;
use std::io::{Read, Write};
use std::net::{TcpListener, TcpStream};
use std::thread;
use std::time::Duration;

fn mock_server(responses: Vec<(u16, &'static str)>) -> (String, thread::JoinHandle<usize>) {
    let listener = TcpListener::bind("127.0.0.1:0").expect("bind mock title server");
    let endpoint = format!("http://{}", listener.local_addr().unwrap());
    listener.set_nonblocking(true).unwrap();
    let handle = thread::spawn(move || {
        let mut requests = 0;
        for (status, response) in responses {
            let deadline = std::time::Instant::now() + Duration::from_secs(3);
            let mut stream = loop {
                match listener.accept() {
                    Ok((stream, _)) => break stream,
                    Err(error) if error.kind() == std::io::ErrorKind::WouldBlock => {
                        if std::time::Instant::now() >= deadline {
                            return requests;
                        }
                        thread::sleep(Duration::from_millis(10));
                    }
                    Err(error) => panic!("accept title request: {error}"),
                }
            };
            read_request(&mut stream).expect("complete title request");
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

fn read_request(stream: &mut TcpStream) -> std::io::Result<Vec<u8>> {
    stream.set_read_timeout(Some(Duration::from_secs(2)))?;
    let mut bytes = Vec::new();
    let mut chunk = [0u8; 1024];
    loop {
        match stream.read(&mut chunk) {
            Ok(0) => {
                return Err(std::io::Error::new(
                    std::io::ErrorKind::UnexpectedEof,
                    "incomplete request",
                ));
            }
            Err(error) => return Err(error),
            Ok(n) => {
                bytes.extend_from_slice(&chunk[..n]);
                if let Some(headers_end) = bytes.windows(4).position(|window| window == b"\r\n\r\n")
                {
                    let content_length = bytes[..headers_end]
                        .split(|byte| *byte == b'\n')
                        .filter_map(|line| {
                            let mut fields = line.splitn(2, |byte| *byte == b':');
                            let name = fields.next()?;
                            let value = fields.next()?;
                            name.eq_ignore_ascii_case(b"content-length").then(|| {
                                std::str::from_utf8(value)
                                    .ok()?
                                    .trim()
                                    .parse::<usize>()
                                    .ok()
                            })
                        })
                        .next()
                        .flatten()
                        .ok_or_else(|| {
                            std::io::Error::new(
                                std::io::ErrorKind::InvalidData,
                                "missing Content-Length",
                            )
                        })?;
                    if bytes.len() >= headers_end + 4 + content_length {
                        return Ok(bytes);
                    }
                }
            }
        }
    }
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

#[test]
fn incomplete_request_is_rejected() {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let mut client = TcpStream::connect(listener.local_addr().unwrap()).unwrap();
    client
        .write_all(b"POST / HTTP/1.1\r\nContent-Length: 4\r\n\r\nx")
        .unwrap();
    client.shutdown(std::net::Shutdown::Write).unwrap();
    let (mut stream, _) = listener.accept().unwrap();
    assert_eq!(
        read_request(&mut stream).unwrap_err().kind(),
        std::io::ErrorKind::UnexpectedEof
    );
}
