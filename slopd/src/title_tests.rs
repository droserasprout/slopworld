use super::{clean, message_content, summarize_with_key, SummaryCache, MAX_CACHE_ENTRIES};
use serde_json::json;
use std::fs;
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
            let _ = read_request(&mut stream);
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

#[test]
fn summary_cache_round_trips_without_storing_the_prompt() {
    let path = std::env::temp_dir().join(format!("slopd-title-cache-{}.toml", std::process::id()));
    let _ = fs::remove_file(&path);

    let cache = SummaryCache::load(path.clone());
    cache
        .insert(
            "codex",
            "fix the parser",
            "Summarise in six words.",
            "test/model",
            "Fix parser",
        )
        .unwrap();
    let raw = fs::read_to_string(&path).unwrap();
    assert!(!raw.contains("fix the parser"));
    assert_eq!(
        cache
            .get("fix the parser", "Summarise in six words.", "test/model")
            .as_deref(),
        Some("Fix parser")
    );
    assert_eq!(cache.latest("codex").as_deref(), Some("Fix parser"));

    let restored = SummaryCache::load(path.clone());
    assert_eq!(
        restored
            .get("fix the parser", "Summarise in six words.", "test/model")
            .as_deref(),
        Some("Fix parser")
    );
    assert_eq!(restored.latest("codex").as_deref(), Some("Fix parser"));
    assert!(restored
        .get("fix the parser", "Summarise in six words.", "other/model")
        .is_none());
    assert!(restored
        .get(
            "fix the parser",
            "Use a different instruction.",
            "test/model"
        )
        .is_none());

    cache.clear_latest("codex").unwrap();
    assert!(cache.latest("codex").is_none());
    let cleared = SummaryCache::load(path.clone());
    assert!(cleared.latest("codex").is_none());
    assert_eq!(
        cleared
            .get("fix the parser", "Summarise in six words.", "test/model")
            .as_deref(),
        Some("Fix parser")
    );
    let _ = fs::remove_file(path);
}

#[test]
fn task_cache_insert_preserves_latest_and_evicts_oldest_entry() {
    let path = std::env::temp_dir().join(format!(
        "slopd-title-task-cache-{}.toml",
        std::process::id()
    ));
    let _ = fs::remove_file(&path);

    let cache = SummaryCache::load(path.clone());
    cache
        .insert(
            "codex",
            "session prompt",
            "Summarise in six words.",
            "test/model",
            "Session title",
        )
        .unwrap();
    for index in 0..=MAX_CACHE_ENTRIES {
        let prompt = format!("task-{index}");
        let title = format!("Task {index}");
        cache
            .insert_cached(&prompt, "Summarise in six words.", "test/model", &title)
            .unwrap();
    }

    assert_eq!(cache.latest("codex").as_deref(), Some("Session title"));
    assert!(cache
        .get("task-0", "Summarise in six words.", "test/model")
        .is_none());
    assert_eq!(
        cache
            .get(
                &format!("task-{MAX_CACHE_ENTRIES}"),
                "Summarise in six words.",
                "test/model"
            )
            .as_deref(),
        Some("Task 1024")
    );

    let _ = fs::remove_file(path);
}
