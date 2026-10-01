//! Bounded HTTP request transport for daemon and CLI tests.
//! Provider fixtures own response sequences; CLI fixtures own Protobuf adaptation.

use std::io::{self, BufRead, BufReader, Read};
use std::net::{TcpListener, TcpStream};
use std::thread;
use std::time::{Duration, Instant};

pub(crate) fn accept_request(listener: &TcpListener, timeout: Duration) -> io::Result<TcpStream> {
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

pub(crate) fn read_request(socket: &TcpStream, timeout: Duration) -> io::Result<(String, Vec<u8>)> {
    let mut reader = BufReader::new(socket.try_clone()?);
    let mut request = String::new();
    let mut content_length = 0;
    let deadline = Instant::now() + timeout;
    loop {
        set_read_deadline(reader.get_ref(), deadline)?;
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
    let mut remaining = body.as_mut_slice();
    while !remaining.is_empty() {
        set_read_deadline(reader.get_ref(), deadline)?;
        let read = reader.read(remaining)?;
        if read == 0 {
            return Err(io::Error::new(
                io::ErrorKind::UnexpectedEof,
                "incomplete request body",
            ));
        }
        remaining = &mut remaining[read..];
    }
    Ok((request, body))
}

fn set_read_deadline(socket: &TcpStream, deadline: Instant) -> io::Result<()> {
    let remaining = deadline
        .checked_duration_since(Instant::now())
        .filter(|remaining| !remaining.is_zero())
        .ok_or_else(|| io::Error::new(io::ErrorKind::TimedOut, "request deadline exceeded"))?;
    socket.set_read_timeout(Some(remaining))
}

#[cfg(test)]
#[path = "test_http_tests.rs"]
mod tests;
