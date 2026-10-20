//! Deadline-bound local HTTP fixtures for CLI transport tests.

use crate::http::Endpoint;
use crate::shared::{http_wire, wire};
use crate::test_http::{accept_request, read_request};
use prost::Message;
use serde_json::Value;
use std::io::Write;
use std::net::{TcpListener, TcpStream};
use std::thread;
use std::time::Duration;

const DEADLINE: Duration = Duration::from_secs(5);
type Server = thread::JoinHandle<Result<String, String>>;

enum Response {
    Wire(String),
    Raw(String, Vec<u8>),
    Redirect(String),
    Truncated,
}

pub(crate) fn serve(status: &str, body: &str) -> (Endpoint, Server) {
    start(status, Response::Wire(body.into()))
}

pub(crate) fn serve_raw(status: &str, content_type: &str, body: &[u8]) -> (Endpoint, Server) {
    start(status, Response::Raw(content_type.into(), body.to_vec()))
}

pub(crate) fn serve_truncated() -> (Endpoint, Server) {
    start("200 OK", Response::Truncated)
}

pub(crate) fn serve_sequence(responses: Vec<(&str, String)>) -> (Endpoint, Server) {
    let responses = responses
        .into_iter()
        .map(|(status, body)| (status.to_owned(), Response::Wire(body)))
        .collect();
    start_sequence(responses)
}

pub(super) fn serve_redirect(status: &str, location: &str) -> (Endpoint, Server) {
    start(status, Response::Redirect(location.into()))
}

fn start(status: &str, response: Response) -> (Endpoint, Server) {
    start_sequence(vec![(status.to_owned(), response)])
}

fn start_sequence(responses: Vec<(String, Response)>) -> (Endpoint, Server) {
    let listener = TcpListener::bind(("127.0.0.1", 0)).unwrap();
    let address = listener.local_addr().unwrap();
    listener.set_nonblocking(true).unwrap();
    let server = thread::spawn(move || {
        let mut requests = String::new();
        for (status, response) in responses {
            let socket = accept_request(&listener, DEADLINE)
                .map_err(|e| format!("HTTP fixture accepting request: {e}"))?;
            requests.push_str(
                &handle(socket, &status, response)
                    .map_err(|error| format!("HTTP fixture: {error}"))?,
            );
        }
        Ok(requests)
    });
    (
        Endpoint {
            url: format!("http://{address}"),
            token: "secret".into(),
        },
        server,
    )
}

fn handle(mut socket: TcpStream, status: &str, response: Response) -> Result<String, String> {
    socket
        .set_read_timeout(Some(DEADLINE))
        .map_err(|e| e.to_string())?;
    socket
        .set_write_timeout(Some(DEADLINE))
        .map_err(|e| e.to_string())?;
    let (mut request, request_body) = read_request(&socket, DEADLINE).map_err(|e| e.to_string())?;
    let parts: Vec<_> = request
        .lines()
        .next()
        .ok_or("missing request line")?
        .split_whitespace()
        .collect();
    let method = *parts.first().ok_or("missing method")?;
    let path = *parts.get(1).ok_or("missing path")?;
    let method = method.to_owned();
    let path = path.to_owned();
    if !request_body.is_empty() {
        if !request
            .to_ascii_lowercase()
            .contains("content-type: application/x-protobuf")
        {
            return Err("request is missing Protobuf content type".into());
        }
        let value =
            http_wire::decode_request(&method, &path, &request_body).map_err(|e| e.to_string())?;
        request.push_str(&value.to_string());
    }
    let (content_type, encoded) = match response {
        Response::Truncated => {
            write!(socket, "HTTP/1.1 {status}\r\nContent-Type: application/x-protobuf\r\nContent-Length: 10\r\nConnection: close\r\n\r\nx")
                .map_err(|e| e.to_string())?;
            return Ok(request);
        }
        Response::Redirect(location) => {
            write!(socket, "HTTP/1.1 {status}\r\nLocation: {location}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n")
                .map_err(|e| e.to_string())?;
            return Ok(request);
        }
        Response::Raw(content_type, bytes) => (content_type, bytes),
        Response::Wire(body) => (
            http_wire::CONTENT_TYPE.to_owned(),
            encode(status, &method, &path, &body)?,
        ),
    };
    write!(socket, "HTTP/1.1 {status}\r\nContent-Type: {content_type}\r\nContent-Length: {}\r\nConnection: close\r\n\r\n", encoded.len()).map_err(|e| e.to_string())?;
    socket.write_all(&encoded).map_err(|e| e.to_string())?;
    Ok(request)
}

fn encode(status: &str, method: &str, path: &str, body: &str) -> Result<Vec<u8>, String> {
    match serde_json::from_str::<Value>(body) {
        Ok(value) if status.starts_with("200") => {
            http_wire::encode_response(method, path, value).map_err(|e| e.to_string())
        }
        Ok(value) => Ok(wire::Error {
            error: value["error"]
                .as_str()
                .ok_or("missing fixture error")?
                .into(),
        }
        .encode_to_vec()),
        Err(_) if !status.starts_with("200") => Ok(wire::Error {
            error: if body.is_empty() {
                format!("refused: {status}")
            } else {
                format!("{status}: {body}")
            },
        }
        .encode_to_vec()),
        Err(_) => Ok(vec![0x80]),
    }
}

/// A redirect target that answers unexpected traffic so a regression cannot hang the client.
pub(super) fn redirect_target() -> (
    String,
    std::sync::mpsc::Sender<()>,
    thread::JoinHandle<bool>,
) {
    let listener = TcpListener::bind(("127.0.0.1", 0)).unwrap();
    listener.set_nonblocking(true).unwrap();
    let location = format!("http://{}/api/health", listener.local_addr().unwrap());
    let (stop, stopped) = std::sync::mpsc::channel();
    let server = thread::spawn(move || {
        let deadline = std::time::Instant::now() + DEADLINE;
        loop {
            match listener.accept() {
                Ok((socket, _)) => {
                    handle(socket, "200 OK", Response::Wire(r#"{"ok":true}"#.into())).unwrap();
                    return true;
                }
                Err(error) if error.kind() == std::io::ErrorKind::WouldBlock => {}
                Err(error) => panic!("redirect target: {error}"),
            }
            if stopped.try_recv().is_ok() {
                return false;
            }
            assert!(
                std::time::Instant::now() < deadline,
                "redirect probe timed out"
            );
            thread::sleep(Duration::from_millis(1));
        }
    });
    (location, stop, server)
}
