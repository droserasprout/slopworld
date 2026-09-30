//! Deadline-bound local HTTP fixtures for CLI transport tests.

use crate::http::Endpoint;
use crate::shared::{http_wire, wire};
use prost::Message;
use serde_json::Value;
use std::io::{self, BufRead, BufReader, Read, Write};
use std::net::{TcpListener, TcpStream};
use std::thread;
use std::time::{Duration, Instant};

const DEADLINE: Duration = Duration::from_secs(5);
type Server = thread::JoinHandle<Result<String, String>>;

enum Response {
    Wire(String),
    Raw(String, Vec<u8>),
}

pub(super) fn serve(status: &str, body: &str) -> (Endpoint, Server) {
    start(status, Response::Wire(body.into()))
}

pub(super) fn serve_raw(status: &str, content_type: &str, body: &[u8]) -> (Endpoint, Server) {
    start(status, Response::Raw(content_type.into(), body.to_vec()))
}

fn start(status: &str, response: Response) -> (Endpoint, Server) {
    let listener = TcpListener::bind(("127.0.0.1", 0)).unwrap();
    let address = listener.local_addr().unwrap();
    listener.set_nonblocking(true).unwrap();
    let status = status.to_owned();
    let server = thread::spawn(move || {
        let socket = accept_request(&listener, DEADLINE)
            .map_err(|e| format!("HTTP fixture accepting request: {e}"))?;
        handle(socket, &status, response).map_err(|error| format!("HTTP fixture: {error}"))
    });
    (
        Endpoint {
            url: format!("http://{address}"),
            token: "secret".into(),
        },
        server,
    )
}

fn accept_request(listener: &TcpListener, timeout: Duration) -> io::Result<TcpStream> {
    let started = Instant::now();
    loop {
        match listener.accept() {
            Ok((socket, _)) => return Ok(socket),
            Err(error) if error.kind() == io::ErrorKind::WouldBlock => {
                if started.elapsed() >= timeout {
                    return Err(io::Error::new(
                        io::ErrorKind::TimedOut,
                        "no request arrived",
                    ));
                }
                thread::sleep(Duration::from_millis(10));
            }
            Err(error) => return Err(error),
        }
    }
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

fn read_request(socket: &TcpStream, timeout: Duration) -> io::Result<(String, Vec<u8>)> {
    let mut reader = BufReader::new(socket.try_clone()?);
    let mut request = String::new();
    let mut content_length = 0;
    let deadline = Instant::now() + timeout;
    loop {
        let remaining = deadline
            .checked_duration_since(Instant::now())
            .filter(|remaining| !remaining.is_zero())
            .ok_or_else(|| io::Error::new(io::ErrorKind::TimedOut, "header deadline exceeded"))?;
        reader.get_ref().set_read_timeout(Some(remaining))?;
        let mut line = String::new();
        if reader.read_line(&mut line)? == 0 {
            return Err(io::Error::new(
                io::ErrorKind::UnexpectedEof,
                "EOF before end of headers",
            ));
        }
        if let Some(value) = line.to_ascii_lowercase().strip_prefix("content-length:") {
            content_length = value
                .trim()
                .parse()
                .map_err(|e| io::Error::new(io::ErrorKind::InvalidData, e))?;
        }
        request.push_str(&line);
        if request.len() > 64 * 1024 || content_length > 1024 * 1024 {
            return Err(io::Error::new(
                io::ErrorKind::InvalidData,
                "request exceeds fixture limit",
            ));
        }
        if line == "\r\n" {
            break;
        }
    }
    let mut body = vec![0; content_length];
    reader.read_exact(&mut body)?;
    Ok((request, body))
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

#[path = "http_fixture_tests.rs"]
mod tests;
