//! Local HTTP fixtures shared by provider and cache tests.
use super::providers::AnthropicCreds;
use crate::test_http::{accept_request, read_request};
use std::io::Write;
use std::net::TcpListener;
use std::path::Path;
use std::time::Duration;

// One request per server. A cache hit must succeed even after this listener closes.
pub(super) fn server(
    status: u16,
    headers: &str,
    body: &str,
) -> (String, std::thread::JoinHandle<String>) {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    listener.set_nonblocking(true).unwrap();
    let url = format!("http://{}/usage", listener.local_addr().unwrap());
    let response = format!(
        "HTTP/1.1 {status} Test\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n{headers}\r\n{body}",
        body.len()
    );
    let handle = std::thread::spawn(move || {
        let mut stream = accept_request(&listener, Duration::from_secs(5)).unwrap();
        stream
            .set_write_timeout(Some(Duration::from_secs(5)))
            .unwrap();
        let (request, _) = read_request(&stream, Duration::from_secs(5)).unwrap();
        stream.write_all(response.as_bytes()).unwrap();
        request
    });
    (url, handle)
}

pub(super) fn credentials(path: &Path) -> AnthropicCreds {
    AnthropicCreds {
        token: "test-anthropic-token".into(),
        plan: "max".into(),
        path: path.into(),
    }
}

pub(super) fn header(request: &str, name: &str) -> Option<String> {
    request
        .lines()
        .filter_map(|line| line.split_once(':'))
        .find_map(|(key, value)| {
            key.eq_ignore_ascii_case(name)
                .then(|| value.trim().to_string())
        })
}
