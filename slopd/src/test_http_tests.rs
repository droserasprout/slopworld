use super::*;
use std::io::Write;
use std::net::Shutdown;

fn pair() -> (TcpStream, TcpStream) {
    let listener = TcpListener::bind(("127.0.0.1", 0)).unwrap();
    let client = TcpStream::connect(listener.local_addr().unwrap()).unwrap();
    (client, listener.accept().unwrap().0)
}

#[test]
fn header_eof_returns_an_error_instead_of_spinning() {
    let (mut client, server) = pair();
    client.write_all(b"GET /api/health HTTP/1.1\r\n").unwrap();
    client.shutdown(Shutdown::Write).unwrap();
    assert_eq!(
        read_request(&server, Duration::from_secs(1))
            .unwrap_err()
            .kind(),
        io::ErrorKind::UnexpectedEof
    );
}

#[test]
fn missing_requests_and_incomplete_headers_have_deadlines() {
    let listener = TcpListener::bind(("127.0.0.1", 0)).unwrap();
    listener.set_nonblocking(true).unwrap();
    assert_eq!(
        accept_request(&listener, Duration::from_millis(20))
            .unwrap_err()
            .kind(),
        io::ErrorKind::TimedOut
    );
    let (mut client, server) = pair();
    client.write_all(b"GET /api/health HTTP/1.1\r\n").unwrap();
    let error = read_request(&server, Duration::from_millis(20)).unwrap_err();
    assert!(matches!(
        error.kind(),
        io::ErrorKind::TimedOut | io::ErrorKind::WouldBlock
    ));
}

#[test]
fn incomplete_request_body_is_rejected() {
    let (mut client, server) = pair();
    client
        .write_all(b"POST / HTTP/1.1\r\nContent-Length: 4\r\n\r\nx")
        .unwrap();
    client.shutdown(Shutdown::Write).unwrap();
    assert_eq!(
        read_request(&server, Duration::from_secs(1))
            .unwrap_err()
            .kind(),
        io::ErrorKind::UnexpectedEof
    );
}

#[test]
fn stalled_body_has_a_deadline() {
    let (mut client, server) = pair();
    client
        .write_all(b"POST / HTTP/1.1\r\nContent-Length: 4\r\n\r\nx")
        .unwrap();
    let error = read_request(&server, Duration::from_millis(20)).unwrap_err();
    assert!(matches!(
        error.kind(),
        io::ErrorKind::TimedOut | io::ErrorKind::WouldBlock
    ));
}
