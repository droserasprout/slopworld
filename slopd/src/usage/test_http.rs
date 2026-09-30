//! Local HTTP fixtures shared by provider and cache tests.
use super::providers::Creds;
use std::io::{Read, Write};
use std::net::TcpListener;
use std::path::Path;
use std::time::{Duration, Instant};

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

pub(super) fn credentials(path: &Path) -> Creds {
    Creds {
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
