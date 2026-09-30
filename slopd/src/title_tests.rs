use super::{clean, message_content, summarize_with_key};
use serde_json::json;
use std::io::{Read, Write};
use std::net::{TcpListener, TcpStream};
use std::thread;
use std::time::Duration;

fn mock_server(responses: Vec<(u16, &'static str)>) -> (String, thread::JoinHandle<()>) {
    let listener = TcpListener::bind("127.0.0.1:0").expect("bind mock title server");
    let endpoint = format!("http://{}", listener.local_addr().unwrap());
    let handle = thread::spawn(move || {
        for (status, response) in responses {
            let (mut stream, _) = listener.accept().expect("accept title request");
            drop(read_request(&mut stream));
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
    });
    (endpoint, handle)
}

fn read_request(stream: &mut TcpStream) -> Vec<u8> {
    stream
        .set_read_timeout(Some(Duration::from_secs(2)))
        .unwrap();
    let mut bytes = Vec::new();
    let mut chunk = [0u8; 1024];
    loop {
        match stream.read(&mut chunk) {
            Ok(0) | Err(_) => break,
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
                            name.eq_ignore_ascii_case(b"content-length")
                                .then(|| std::str::from_utf8(value).ok()?.trim().parse().ok())
                        })
                        .next()
                        .flatten()
                        .unwrap_or(0);
                    if bytes.len() >= headers_end + 4 + content_length {
                        break;
                    }
                }
            }
        }
    }
    bytes
}

#[test]
fn cleans_model_decoration_and_caps_length() {
    assert_eq!(
        clean("  \"Add Codex status titles.\"  ").as_deref(),
        Some("Add Codex status titles")
    );
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
    server.join().unwrap();
    assert_eq!(title.unwrap(), "Fix parser");
}
