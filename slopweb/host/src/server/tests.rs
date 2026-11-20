//! Real-loopback contracts for static containment, provenance and authenticated transports.
use super::*;
use axum::{
    body::to_bytes,
    http::{HeaderMap, Method},
    routing::any,
};
use futures::{SinkExt, StreamExt};
use std::{
    sync::atomic::{AtomicUsize, Ordering},
    time::Duration,
};
use tempfile::TempDir;
use tokio::{
    io::{AsyncReadExt, AsyncWriteExt},
    sync::mpsc,
};
use tokio_tungstenite::tungstenite::{self, client::IntoClientRequest};

struct Running {
    url: String,
    task: tokio::task::JoinHandle<()>,
    _root: Option<TempDir>,
}
impl Drop for Running {
    fn drop(&mut self) {
        self.task.abort();
    }
}
async fn listen(router: Router) -> Running {
    let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let url = format!("http://{}", listener.local_addr().unwrap());
    let task = tokio::spawn(async move {
        axum::serve(listener, router).await.unwrap();
    });
    Running {
        url,
        task,
        _root: None,
    }
}
async fn host(connection: ConnectionSource, public_origin: Option<&str>) -> Running {
    let root = tempfile::tempdir().unwrap();
    std::fs::write(root.path().join("index.html"), "workspace").unwrap();
    let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let port = listener.local_addr().unwrap().port();
    let config = ServerConfig {
        root: root.path().to_owned(),
        port,
        public_origin: public_origin.map(str::to_owned),
        connection,
    };
    let router = router(config, port, None).await.unwrap();
    let task = tokio::spawn(async move {
        axum::serve(listener, router).await.unwrap();
    });
    Running {
        url: format!("http://127.0.0.1:{port}"),
        task,
        _root: Some(root),
    }
}
fn direct(url: &str) -> ConnectionSource {
    ConnectionSource::Direct {
        url: url.to_owned(),
        token: Some("host-secret".into()),
        token_file: None,
    }
}
fn client() -> reqwest::Client {
    reqwest::Client::builder()
        .no_proxy()
        .redirect(reqwest::redirect::Policy::none())
        .build()
        .unwrap()
}

#[tokio::test]
async fn assets_stream_head_and_reject_symlink_escape_or_rebinding() {
    let host = host(ConnectionSource::Unconfigured, None).await;
    let root = host._root.as_ref().unwrap().path();
    let outside = tempfile::NamedTempFile::new().unwrap();
    std::os::unix::fs::symlink(outside.path(), root.join("escape")).unwrap();
    std::os::unix::fs::symlink("index.html", root.join("inside")).unwrap();
    let data = vec![37; 2 * 1024 * 1024];
    std::fs::write(root.join("large.wasm"), &data).unwrap();
    let client = client();
    let page = client.get(&host.url).send().await.unwrap();
    assert_eq!(
        page.headers()["cross-origin-embedder-policy"],
        "require-corp"
    );
    assert_eq!(page.headers()["cache-control"], "private, no-cache");
    assert_eq!(page.text().await.unwrap(), "workspace");
    assert_eq!(
        client
            .get(format!("{}/inside", host.url))
            .send()
            .await
            .unwrap()
            .text()
            .await
            .unwrap(),
        "workspace"
    );
    for (path, status) in [
        ("/escape", 403),
        ("/missing", 404),
        ("/%2e%2e%2fetc/passwd", 403),
    ] {
        assert_eq!(
            client
                .get(format!("{}{path}", host.url))
                .send()
                .await
                .unwrap()
                .status(),
            status
        );
    }
    assert_eq!(
        client
            .get(&host.url)
            .header("host", "evil.invalid")
            .send()
            .await
            .unwrap()
            .status(),
        403
    );
    assert_eq!(client.post(&host.url).send().await.unwrap().status(), 403);
    let head = client
        .head(format!("{}/large.wasm", host.url))
        .send()
        .await
        .unwrap();
    assert_eq!(head.headers()["content-length"], data.len().to_string());
    assert_eq!(
        head.headers()["x-slop-decoded-length"],
        data.len().to_string()
    );
    assert_eq!(head.headers()["content-type"], "application/wasm");
    assert!(head.bytes().await.unwrap().is_empty());
    assert_eq!(
        client
            .get(format!("{}/large.wasm", host.url))
            .send()
            .await
            .unwrap()
            .bytes()
            .await
            .unwrap()
            .as_ref(),
        data
    );
    drop(
        client
            .get(format!("{}/large.wasm", host.url))
            .send()
            .await
            .unwrap(),
    );
    assert_eq!(client.get(&host.url).send().await.unwrap().status(), 200);
}

#[tokio::test]
async fn http_preserves_protobuf_and_status_but_isolates_credentials() {
    for public in [None, Some("https://workspace.example.com")] {
        let (tx, mut seen) = mpsc::unbounded_channel();
        let upstream = listen(Router::new().fallback(any(move |request: Request| {
            let tx = tx.clone();
            async move {
                let (parts, body) = request.into_parts();
                tx.send((parts, to_bytes(body, 1024).await.unwrap()))
                    .unwrap();
                Response::builder()
                    .status(409)
                    .header("x-slop-token", "private-upstream")
                    .header("set-cookie", "private=1")
                    .body(Body::from(vec![8, 1, 255]))
                    .unwrap()
            }
        })))
        .await;
        let prefix = if public.is_some() {
            "/team%20one/slopd-demo"
        } else {
            ""
        };
        let host = host(direct(&format!("{}{prefix}/", upstream.url)), public).await;
        let origin = public.unwrap_or(&host.url);
        let client = client();
        for provenance in [None, Some("http://evil.invalid")] {
            let mut req = client.post(format!("{}/host/api/sessions", host.url));
            if let Some(origin) = provenance {
                req = req.header("origin", origin);
            }
            assert_eq!(req.send().await.unwrap().status(), 403);
        }
        assert!(seen.try_recv().is_err());
        let response = client
            .post(format!("{}/host/api/sessions", host.url))
            .header("origin", origin)
            .header("x-slop-token", "browser-secret")
            .header("authorization", "private")
            .header("cookie", "private=1")
            .body(vec![10, 2, 97, 98])
            .send()
            .await
            .unwrap();
        assert_eq!(response.status(), 409);
        assert!(!response.headers().contains_key("x-slop-token"));
        assert!(!response.headers().contains_key("set-cookie"));
        assert_eq!(response.bytes().await.unwrap().as_ref(), [8, 1, 255]);
        let (parts, body) = seen.recv().await.unwrap();
        assert_eq!(parts.uri.to_string(), format!("{prefix}/api/sessions"));
        assert_eq!(parts.method, Method::POST);
        assert_eq!(parts.headers["x-slop-token"], "host-secret");
        assert!(!parts.headers.contains_key("cookie"));
        assert!(!parts.headers.contains_key("authorization"));
        assert!(!parts.headers.contains_key("origin"));
        assert_eq!(body.as_ref(), [10, 2, 97, 98]);
        let search = "/api/search?q=token%3D";
        let response = client
            .get(format!("{}/host{search}", host.url))
            .header("origin", origin)
            .send()
            .await
            .unwrap();
        assert_eq!(response.status(), 409);
        assert_eq!(
            seen.recv().await.unwrap().0.uri.to_string(),
            format!("{prefix}{search}")
        );
        for path in [
            "/host/api/health?token=bad",
            "/host/api/health?%74oken=bad",
            "/host/api/%2e%2e%2fsecret",
        ] {
            assert_eq!(
                client
                    .get(format!("{}{path}", host.url))
                    .header("origin", origin)
                    .send()
                    .await
                    .unwrap()
                    .status(),
                400
            );
        }
        assert!(seen.try_recv().is_err());
    }
}

#[test]
fn loopback_on_the_default_http_port_accepts_both_authority_forms() {
    let default = security::Boundary::new(80, None).unwrap();
    let other = security::Boundary::new(8787, None).unwrap();
    for name in ["127.0.0.1", "localhost"] {
        for host in [name.to_owned(), format!("{name}:80")] {
            let mut headers = HeaderMap::new();
            headers.insert("host", host.parse().unwrap());
            headers.insert("origin", format!("http://{name}").parse().unwrap());
            assert!(default.admits_host(&headers));
            assert!(default.same_origin(&headers));
            assert!(!other.admits_host(&headers));
        }
    }
}

#[tokio::test]
async fn provenance_and_public_origin_are_explicit() {
    for invalid in [
        "http://example.com",
        "https://example.com/",
        "https://example.com/path",
        "https://user@example.com",
        "https://example.com:443",
    ] {
        assert!(security::Boundary::new(8787, Some(invalid.into())).is_err());
    }
    let boundary = security::Boundary::new(8787, None).unwrap();
    let mut headers = HeaderMap::new();
    headers.insert("host", "localhost:8787".parse().unwrap());
    assert!(!boundary.same_origin(&headers));
    headers.insert(
        "referer",
        "http://localhost:8787/workspace/".parse().unwrap(),
    );
    assert!(boundary.same_origin(&headers));
    headers.insert("origin", "null".parse().unwrap());
    assert!(!boundary.same_origin(&headers));
    headers.insert("origin", "http://localhost:8787".parse().unwrap());
    assert!(boundary.same_origin(&headers));
    headers.insert("sec-fetch-site", "cross-site".parse().unwrap());
    assert!(!boundary.same_origin(&headers));
    headers.insert("sec-fetch-site", "same-origin".parse().unwrap());
    headers.append("origin", "http://localhost:8787".parse().unwrap());
    assert!(!boundary.same_origin(&headers));
    let host = host(
        ConnectionSource::Unconfigured,
        Some("https://workspace.example.com"),
    )
    .await;
    let client = client();
    assert_eq!(
        client
            .get(&host.url)
            .header("host", "workspace.example.com")
            .send()
            .await
            .unwrap()
            .status(),
        200
    );
    assert_eq!(
        client
            .get(format!("{}/host/health", host.url))
            .header("origin", &host.url)
            .header("x-forwarded-host", "workspace.example.com")
            .send()
            .await
            .unwrap()
            .status(),
        403
    );
    assert_eq!(
        client
            .get(format!("{}/host/health", host.url))
            .header("origin", "https://workspace.example.com")
            .send()
            .await
            .unwrap()
            .status(),
        503
    );
}

#[tokio::test]
async fn descriptor_rotation_fails_closed_and_never_replays_mutations() {
    let calls = Arc::new(AtomicUsize::new(0));
    let calls_copy = calls.clone();
    let (tx, mut seen) = mpsc::unbounded_channel();
    let upstream = listen(Router::new().fallback(any(move |headers: HeaderMap| {
        calls_copy.fetch_add(1, Ordering::SeqCst);
        tx.send(headers["x-slop-token"].to_str().unwrap().to_owned())
            .unwrap();
        async { StatusCode::UNAUTHORIZED }
    })))
    .await;
    let root = tempfile::tempdir().unwrap();
    let path = root.path().join("endpoint.toml");
    let write = |token| {
        std::fs::write(
            &path,
            format!("url = {:?}\ntoken = {token:?}\n", upstream.url),
        )
        .unwrap()
    };
    write("first");
    let host = host(ConnectionSource::Descriptor(path.clone()), None).await;
    let client = client();
    let send = || {
        client
            .post(format!("{}/host/api/workers", host.url))
            .header("origin", &host.url)
            .send()
    };
    assert_eq!(send().await.unwrap().status(), 401);
    assert_eq!(seen.recv().await.unwrap(), "first");
    assert_eq!(calls.load(Ordering::SeqCst), 1);
    write("rotated");
    assert_eq!(send().await.unwrap().status(), 401);
    assert_eq!(seen.recv().await.unwrap(), "rotated");
    std::fs::write(&path, "invalid toml").unwrap();
    assert_eq!(send().await.unwrap().status(), 503);
    std::fs::remove_file(&path).unwrap();
    assert_eq!(send().await.unwrap().status(), 503);
    assert_eq!(calls.load(Ordering::SeqCst), 2);
}

#[tokio::test]
async fn token_file_refresh_and_invalid_connections_fail_closed() {
    let token = tempfile::NamedTempFile::new().unwrap();
    std::fs::write(token.path(), " first\n").unwrap();
    let source = ConnectionSource::Direct {
        url: "http://127.0.0.1".into(),
        token: Some("stale".into()),
        token_file: Some(token.path().into()),
    };
    assert_eq!(source.snapshot().await.unwrap().token, "first");
    std::fs::write(token.path(), "second").unwrap();
    assert_eq!(source.snapshot().await.unwrap().token, "second");
    std::fs::remove_file(token.path()).unwrap();
    assert!(source.snapshot().await.is_err());
    for url in [
        "ftp://host",
        "http://user:password@host",
        "http://host/demo?q=1",
        "http://host/demo#part",
        "invalid",
    ] {
        assert!(direct(url).snapshot().await.is_err());
    }
}

#[tokio::test]
async fn upstream_redirects_and_disconnects_are_not_replayed() {
    let calls = Arc::new(AtomicUsize::new(0));
    let copy = calls.clone();
    let upstream = listen(Router::new().fallback(any(move || {
        copy.fetch_add(1, Ordering::SeqCst);
        async {
            Response::builder()
                .status(307)
                .header("location", "/api/retry")
                .body(Body::empty())
                .unwrap()
        }
    })))
    .await;
    let host = host(direct(&upstream.url), None).await;
    let client = client();
    assert_eq!(
        client
            .post(format!("{}/host/api/workers", host.url))
            .header("origin", &host.url)
            .send()
            .await
            .unwrap()
            .status(),
        502
    );
    assert_eq!(calls.load(Ordering::SeqCst), 1);
    upstream.task.abort();
    assert_eq!(
        client
            .post(format!("{}/host/api/workers", host.url))
            .header("origin", &host.url)
            .send()
            .await
            .unwrap()
            .status(),
        502
    );
}

fn ws_request(host: &Running, origin: &str) -> tungstenite::http::Request<()> {
    let mut request = format!("{}/host/ws", host.url.replace("http:", "ws:"))
        .into_client_request()
        .unwrap();
    request
        .headers_mut()
        .insert("origin", origin.parse().unwrap());
    request.headers_mut().insert(
        "sec-websocket-protocol",
        "slopworld.protobuf.v2".parse().unwrap(),
    );
    request
        .headers_mut()
        .insert("x-slop-token", "browser-secret".parse().unwrap());
    request
}

#[tokio::test]
async fn websocket_authenticates_rotates_and_relays_large_binary_frames() {
    let (tx, mut seen) = mpsc::unbounded_channel();
    let upstream = listen(Router::new().route(
        "/slopd-demo/ws",
        any(
            move |headers: HeaderMap, upgrade: axum::extract::ws::WebSocketUpgrade| {
                tx.send(headers).unwrap();
                async move {
                    upgrade.protocols(["slopworld.protobuf.v2"]).on_upgrade(
                        |mut socket| async move {
                            while let Some(Ok(message)) = socket.recv().await {
                                if socket.send(message).await.is_err() {
                                    break;
                                }
                            }
                        },
                    )
                }
            },
        ),
    ))
    .await;
    let descriptor = tempfile::NamedTempFile::new().unwrap();
    let endpoint = format!("{}/slopd-demo", upstream.url);
    for public in [None, Some("https://workspace.example.com")] {
        let host = host(
            ConnectionSource::Descriptor(descriptor.path().into()),
            public,
        )
        .await;
        for token in ["host-secret", "rotated"] {
            std::fs::write(
                descriptor.path(),
                format!("url = {endpoint:?}\ntoken = {token:?}\n"),
            )
            .unwrap();
            let origin = public.unwrap_or(&host.url);
            let mut request = ws_request(&host, origin);
            if let Some(public) = public {
                request.headers_mut().insert(
                    "host",
                    public.trim_start_matches("https://").parse().unwrap(),
                );
            }
            let (mut socket, response) = tokio_tungstenite::connect_async(request).await.unwrap();
            assert_eq!(
                response.headers()["sec-websocket-protocol"],
                "slopworld.protobuf.v2"
            );
            let headers = seen.recv().await.unwrap();
            assert_eq!(headers["x-slop-token"], token);
            assert!(!headers.contains_key("origin"));
            let payload = vec![0x80; 1024 * 1024];
            socket
                .send(tungstenite::Message::Binary(payload.clone()))
                .await
                .unwrap();
            let response = tokio::time::timeout(Duration::from_secs(5), socket.next())
                .await
                .unwrap()
                .unwrap()
                .unwrap();
            assert_eq!(response, tungstenite::Message::Binary(payload));
            socket.close(None).await.unwrap();
        }
        let error = tokio_tungstenite::connect_async(ws_request(&host, "http://evil.invalid"))
            .await
            .unwrap_err();
        assert!(matches!(error, tungstenite::Error::Http(response) if response.status() == 403));
        assert!(seen.try_recv().is_err());
    }
}

#[tokio::test]
async fn websocket_rejects_missing_protocol_upstream_failure_and_rebinding() {
    let upstream = listen(Router::new().fallback(any(|| async { StatusCode::UNAUTHORIZED }))).await;
    let host = host(direct(&upstream.url), None).await;
    for (kind, expected) in [("host", 403), ("protocol", 403), ("upstream", 502)] {
        let mut request = ws_request(&host, &host.url);
        if kind == "host" {
            request
                .headers_mut()
                .insert("host", "evil.invalid".parse().unwrap());
        }
        if kind == "protocol" {
            request.headers_mut().remove("sec-websocket-protocol");
        }
        let error = tokio_tungstenite::connect_async(request).await.unwrap_err();
        assert!(
            matches!(error, tungstenite::Error::Http(response) if response.status() == expected)
        );
    }
}

#[tokio::test]
async fn browser_disconnect_cancels_pending_http_upstream() {
    let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let upstream_url = format!("http://{}", listener.local_addr().unwrap());
    let host = host(direct(&upstream_url), None).await;
    let mut browser = tokio::net::TcpStream::connect(host.url.trim_start_matches("http://"))
        .await
        .unwrap();
    browser.write_all(format!("POST /host/api/workers HTTP/1.1\r\nHost: {}\r\nOrigin: {}\r\nContent-Length: 0\r\n\r\n", host.url.trim_start_matches("http://"), host.url).as_bytes()).await.unwrap();
    let (mut upstream, _) = listener.accept().await.unwrap();
    let mut bytes = [0; 4096];
    assert!(upstream.read(&mut bytes).await.unwrap() > 0);
    drop(browser);
    assert_eq!(
        tokio::time::timeout(Duration::from_secs(3), upstream.read(&mut bytes))
            .await
            .expect("disconnect must cancel upstream")
            .unwrap(),
        0
    );
}

#[tokio::test]
async fn websocket_preserves_upstream_close_code_and_reason() {
    let upstream = listen(Router::new().route(
        "/ws",
        any(|upgrade: axum::extract::ws::WebSocketUpgrade| async {
            upgrade
                .protocols(["slopworld.protobuf.v2"])
                .on_upgrade(|mut socket| async move {
                    socket
                        .send(axum::extract::ws::Message::Close(Some(
                            axum::extract::ws::CloseFrame {
                                code: 1013,
                                reason: "overloaded".into(),
                            },
                        )))
                        .await
                        .unwrap();
                    let _ = socket.recv().await;
                })
        }),
    ))
    .await;
    let host = host(direct(&upstream.url), None).await;
    let (mut browser, _) = tokio_tungstenite::connect_async(ws_request(&host, &host.url))
        .await
        .unwrap();
    let message = tokio::time::timeout(Duration::from_secs(3), browser.next())
        .await
        .unwrap()
        .unwrap()
        .unwrap();
    let tungstenite::Message::Close(Some(close)) = message else {
        panic!("close frame expected");
    };
    assert_eq!(u16::from(close.code), 1013);
    assert_eq!(close.reason, "overloaded");
    let _ = browser.next().await;
}

#[tokio::test]
async fn websocket_browser_disconnect_releases_upstream() {
    let (tx, mut closed) = mpsc::unbounded_channel();
    let upstream = listen(Router::new().route(
        "/ws",
        any(move |upgrade: axum::extract::ws::WebSocketUpgrade| {
            let tx = tx.clone();
            async {
                upgrade.protocols(["slopworld.protobuf.v2"]).on_upgrade(
                    move |mut socket| async move {
                        while let Some(Ok(_)) = socket.recv().await {}
                        tx.send(()).unwrap();
                    },
                )
            }
        }),
    ))
    .await;
    let host = host(direct(&upstream.url), None).await;
    let (browser, _) = tokio_tungstenite::connect_async(ws_request(&host, &host.url))
        .await
        .unwrap();
    drop(browser);
    tokio::time::timeout(Duration::from_secs(3), closed.recv())
        .await
        .unwrap()
        .unwrap();
}

#[tokio::test]
async fn websocket_does_not_accept_an_upstream_without_selected_protocol() {
    let upstream = listen(Router::new().route(
        "/ws",
        any(|upgrade: axum::extract::ws::WebSocketUpgrade| async {
            upgrade.on_upgrade(|_socket| async {})
        }),
    ))
    .await;
    let host = host(direct(&upstream.url), None).await;
    let error = tokio_tungstenite::connect_async(ws_request(&host, &host.url))
        .await
        .unwrap_err();
    assert!(matches!(error, tungstenite::Error::Http(response) if response.status() == 502));
}

#[tokio::test]
async fn failed_upstream_post_is_sent_exactly_once() {
    let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let host = host(
        direct(&format!("http://{}", listener.local_addr().unwrap())),
        None,
    )
    .await;
    let url = host.url.clone();
    let pending = tokio::spawn(async move {
        client()
            .post(format!("{url}/host/api/workers"))
            .header("origin", url)
            .body("mutation")
            .send()
            .await
            .unwrap()
            .status()
    });
    let (mut upstream, _) = listener.accept().await.unwrap();
    let mut received = Vec::new();
    let mut bytes = [0; 4096];
    while !received.ends_with(b"mutation") {
        let count = upstream.read(&mut bytes).await.unwrap();
        assert_ne!(count, 0);
        received.extend_from_slice(&bytes[..count]);
    }
    drop(upstream);
    assert_eq!(pending.await.unwrap(), 502);
    assert!(
        tokio::time::timeout(Duration::from_millis(100), listener.accept())
            .await
            .is_err()
    );
}

#[tokio::test]
async fn malformed_or_cross_origin_font_routes_never_deliver_private_data() {
    for public in [None, Some("https://workspace.example.com")] {
        let host = host(ConnectionSource::Unconfigured, public).await;
        for path in [
            "/host/fonts",
            "/host/fonts/catalog",
            "/host/fonts/files/../../secret",
            "/host/fonts/files/%2e%2e%2fsecret",
        ] {
            let url = format!("{}{path}", host.url);
            let response = client()
                .get(&url)
                .header("origin", "https://evil.invalid")
                .send()
                .await
                .unwrap();
            assert!(!response.status().is_success());
        }
    }
}

#[tokio::test]
// Tungstenite fixes the handshake callback error type to an unboxed HTTP response.
#[allow(clippy::result_large_err)]
async fn websocket_backpressure_keeps_reverse_direction_live_and_resumes_losslessly() {
    use std::os::fd::AsRawFd;
    let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let host = host(
        direct(&format!("http://{}", listener.local_addr().unwrap())),
        None,
    )
    .await;
    let (resume, paused) = tokio::sync::oneshot::channel();
    let upstream = tokio::spawn(async move {
        let (stream, _) = listener.accept().await.unwrap();
        let mut socket = tokio_tungstenite::accept_hdr_async(
            stream,
            |_request: &tungstenite::handshake::server::Request,
             mut response: tungstenite::handshake::server::Response| {
                response.headers_mut().insert(
                    "sec-websocket-protocol",
                    "slopworld.protobuf.v2".parse().unwrap(),
                );
                Ok(response)
            },
        )
        .await
        .unwrap();
        // Remain far below the 16 MiB payload without shrinking below loopback's
        // packet size. A 4 KiB buffer can force kernel-specific zero-window/retransmit
        // behavior even after reads resume, obscuring the relay behavior under test.
        let receive_buffer: libc::c_int = 256 * 1024;
        // SAFETY: the live socket and integer option remain valid during setsockopt.
        assert_eq!(
            unsafe {
                libc::setsockopt(
                    socket.get_ref().as_raw_fd(),
                    libc::SOL_SOCKET,
                    libc::SO_RCVBUF,
                    (&receive_buffer as *const libc::c_int).cast(),
                    std::mem::size_of_val(&receive_buffer) as libc::socklen_t,
                )
            },
            0
        );
        // No upstream reads occur until the browser observes this reverse message.
        // Readiness proves the relay has started its large, capacity-limited write.
        socket.get_ref().readable().await.unwrap();
        socket
            .send(tungstenite::Message::Text(
                "reverse direction remains live".into(),
            ))
            .await
            .unwrap();
        paused.await.unwrap();
        let message = socket.next().await.unwrap().unwrap();
        assert_eq!(
            message,
            tungstenite::Message::Binary(vec![0x93; 16 * 1024 * 1024])
        );
        socket
            .send(tungstenite::Message::Text("complete".into()))
            .await
            .unwrap();
    });
    let (browser, _) = tokio_tungstenite::connect_async(ws_request(&host, &host.url))
        .await
        .unwrap();
    let (mut writer, mut reader) = browser.split();
    let sending = tokio::spawn(async move {
        writer
            .send(tungstenite::Message::Binary(vec![0x93; 16 * 1024 * 1024]))
            .await
            .unwrap();
        writer
    });
    let alive = tokio::time::timeout(Duration::from_secs(5), reader.next())
        .await
        .expect("reverse forwarding must not block behind the large write")
        .unwrap()
        .unwrap();
    assert_eq!(
        alive,
        tungstenite::Message::Text("reverse direction remains live".into())
    );
    resume.send(()).unwrap();
    assert_eq!(
        tokio::time::timeout(Duration::from_secs(10), reader.next())
            .await
            .unwrap()
            .unwrap()
            .unwrap(),
        tungstenite::Message::Text("complete".into())
    );
    drop(sending.await.unwrap());
    upstream.await.unwrap();
}

#[tokio::test]
async fn asset_reload_revalidates_and_replacements_invalidate() {
    let host = host(ConnectionSource::Unconfigured, None).await;
    let client = client();
    let first = client.get(&host.url).send().await.unwrap();
    let etag = first.headers()["etag"].to_str().unwrap().to_owned();
    assert_eq!(first.text().await.unwrap(), "workspace");
    for method in [Method::GET, Method::HEAD] {
        for validator in [
            etag.clone(),
            etag[2..].to_owned(),
            format!("\"other\", {etag}"),
            "*".into(),
        ] {
            let response = client
                .request(method.clone(), &host.url)
                .header("if-none-match", validator)
                .send()
                .await
                .unwrap();
            assert_eq!(response.status(), StatusCode::NOT_MODIFIED);
            assert_eq!(response.headers()["etag"], etag);
            assert_eq!(response.headers()["cache-control"], "private, no-cache");
            assert_eq!(
                response.headers()["cross-origin-embedder-policy"],
                "require-corp"
            );
            assert!(response.bytes().await.unwrap().is_empty());
        }
    }
    let root = host._root.as_ref().unwrap().path();
    std::fs::write(root.join("replacement"), "new bytes").unwrap();
    std::fs::rename(root.join("replacement"), root.join("index.html")).unwrap();
    let changed = client
        .get(&host.url)
        .header("if-none-match", &etag)
        .send()
        .await
        .unwrap();
    assert_eq!(changed.status(), StatusCode::OK);
    assert_ne!(changed.headers()["etag"], etag);
    assert_eq!(changed.text().await.unwrap(), "new bytes");
    let missing = client
        .get(format!("{}/missing", host.url))
        .header("if-none-match", "*")
        .send()
        .await
        .unwrap();
    assert_eq!(missing.status(), StatusCode::NOT_FOUND);
    assert_eq!(missing.headers()["cache-control"], "no-store");
}

#[tokio::test]
async fn only_sdk_fingerprinted_framework_resources_have_a_freshness_lifetime() {
    let host = host(ConnectionSource::Unconfigured, None).await;
    let root = host._root.as_ref().unwrap().path();
    let client = client();
    for (name, immutable) in [
        ("_framework/System.Runtime.b9bqw25qxw.wasm", true),
        ("_framework/dotnet.runtime.5nc8i5dl74.js", true),
        ("_framework/icudt_CJK.tjcz0u77k5.dat", true),
        ("_framework/dotnet.js", false),
        ("_framework/dotnet.native.wasm", false),
        ("_framework/System.Runtime.wasm", false),
        ("_framework/dotnet.native.1234.wasm", false),
        ("_framework/dotnet.native.0123456789.wasm.gz", false),
        ("_framework/runtime.0123456789.json", false),
        ("game/runtime.0123456789.wasm", false),
    ] {
        let path = root.join(name);
        std::fs::create_dir_all(path.parent().unwrap()).unwrap();
        std::fs::write(path, name).unwrap();
        let result = client
            .get(format!("{}/{name}", host.url))
            .send()
            .await
            .unwrap();
        assert_eq!(result.status(), 200);
        assert_eq!(
            result.headers()["cache-control"],
            if immutable {
                "private, max-age=31536000, immutable"
            } else {
                "private, no-cache"
            },
            "{name}"
        );
    }
}

#[tokio::test]
async fn framework_gzip_negotiates_lengths_mime_and_cache_validators() {
    let host = host(ConnectionSource::Unconfigured, None).await;
    let root = host._root.as_ref().unwrap().path();
    let framework = root.join("_framework");
    std::fs::create_dir(&framework).unwrap();
    let immutable = "private, max-age=31536000, immutable";
    let plain = b"wasm".repeat(32);
    // gzip of the identity bytes, with no filename or timestamp.
    let gzip = [
        31, 139, 8, 0, 0, 0, 0, 0, 2, 255, 43, 79, 44, 206, 45, 31, 64, 12, 0, 66, 155, 216, 32,
        128, 0, 0, 0,
    ];
    let file = framework.join("dotnet.native.a6k1euv12n.wasm");
    std::fs::write(&file, &plain).unwrap();
    std::fs::write(framework.join("dotnet.native.a6k1euv12n.wasm.gz"), gzip).unwrap();
    let url = format!("{}/_framework/dotnet.native.a6k1euv12n.wasm", host.url);
    let client = client();
    let raw = client.get(&url).send().await.unwrap();
    assert_eq!(raw.headers()["cache-control"], immutable);
    let raw_tag = raw.headers()["etag"].clone();
    assert_eq!(raw.headers()["vary"], "Accept-Encoding");
    assert!(!raw.headers().contains_key("content-encoding"));
    assert_eq!(raw.bytes().await.unwrap().as_ref(), plain);

    for encoding in ["br, GZip", "br;q=1, *;q=0.5", "gzip;Q=0.125, identity;q=0"] {
        let encoded = client
            .get(&url)
            .header("accept-encoding", encoding)
            .header("if-none-match", &raw_tag)
            .send()
            .await
            .unwrap();
        assert_eq!(encoded.status(), 200);
        let tag = encoded.headers()["etag"].clone();
        assert_ne!(tag, raw_tag);
        assert_eq!(encoded.headers()["content-encoding"], "gzip");
        assert_eq!(encoded.headers()["content-type"], "application/wasm");
        assert_eq!(encoded.headers()["content-length"], gzip.len().to_string());
        assert_eq!(
            encoded.headers()["x-slop-decoded-length"],
            plain.len().to_string()
        );
        assert_eq!(encoded.headers()["cache-control"], immutable);
        assert_eq!(encoded.headers()["vary"], "Accept-Encoding");
        assert_eq!(encoded.bytes().await.unwrap().as_ref(), gzip);
        let head = client
            .head(&url)
            .header("accept-encoding", encoding)
            .send()
            .await
            .unwrap();
        assert_eq!(head.headers()["cache-control"], immutable);
        assert_eq!(head.headers()["etag"], tag);
        assert_eq!(head.headers()["content-length"], gzip.len().to_string());
        assert!(head.bytes().await.unwrap().is_empty());
        let validated = client
            .get(&url)
            .header("accept-encoding", encoding)
            .header("if-none-match", tag)
            .send()
            .await
            .unwrap();
        assert_eq!(validated.status(), 304);
        assert_eq!(validated.headers()["cache-control"], immutable);
        assert_eq!(validated.headers()["vary"], "Accept-Encoding");
        assert!(validated.bytes().await.unwrap().is_empty());
    }
    for encoding in [
        "gzip;q=0,*;q=1",
        "br",
        "gzip;q=0.5,identity;q=0.6",
        "gzip;q=1.2",
    ] {
        let raw = client
            .get(&url)
            .header("accept-encoding", encoding)
            .send()
            .await
            .unwrap();
        assert_eq!(raw.headers()["etag"], raw_tag);
        assert_eq!(raw.bytes().await.unwrap().as_ref(), plain);
    }
    std::fs::remove_file(framework.join("dotnet.native.a6k1euv12n.wasm.gz")).unwrap();
    let fallback = client
        .get(&url)
        .header("accept-encoding", "gzip")
        .send()
        .await
        .unwrap();
    assert_eq!(fallback.headers()["etag"], raw_tag);
    let unavailable = client
        .get(&url)
        .header("accept-encoding", "gzip, identity;q=0")
        .send()
        .await
        .unwrap();
    assert_eq!(unavailable.status(), 406);
    assert_eq!(unavailable.headers()["cache-control"], "no-store");
    assert_eq!(unavailable.headers()["vary"], "Accept-Encoding");
}

#[tokio::test]
async fn gzip_siblings_obey_static_containment_and_require_a_framework_identity_file() {
    let host = host(ConnectionSource::Unconfigured, None).await;
    let root = host._root.as_ref().unwrap().path();
    let framework = root.join("_framework");
    std::fs::create_dir(&framework).unwrap();
    let outside = tempfile::NamedTempFile::new().unwrap();
    std::fs::write(framework.join("loader.js"), b"script").unwrap();
    std::os::unix::fs::symlink(outside.path(), framework.join("loader.js.gz")).unwrap();
    std::fs::write(framework.join("missing.js.gz"), b"orphan").unwrap();
    std::fs::write(root.join("index.html.gz"), b"outside framework").unwrap();
    let client = client();
    for (path, status) in [
        ("/_framework/loader.js", 403),
        ("/_framework/missing.js", 404),
    ] {
        let response = client
            .get(format!("{}{path}", host.url))
            .header("accept-encoding", "gzip")
            .header("if-none-match", "*")
            .send()
            .await
            .unwrap();
        assert_eq!(response.status(), status);
    }
    let page = client
        .get(&host.url)
        .header("accept-encoding", "gzip")
        .send()
        .await
        .unwrap();
    assert!(!page.headers().contains_key("content-encoding"));
    assert_eq!(page.text().await.unwrap(), "workspace");
}

#[tokio::test]
async fn eco_content_validates_across_roots_and_stale_or_unusable_bindings_fall_back() {
    use crate::archive_identity::{ARCHIVE, MARKER, publish};
    use sha2::{Digest, Sha256};
    let first = host(ConnectionSource::Unconfigured, None).await;
    let second = host(ConnectionSource::Unconfigured, None).await;
    let digest = format!("{:x}", Sha256::digest(b"archive"));
    let tag = format!("\"sha256-{digest}\"");
    let client = client();
    for host in [&first, &second] {
        let root = host._root.as_ref().unwrap().path();
        std::fs::create_dir(root.join("assets")).unwrap();
        std::fs::write(root.join(ARCHIVE), b"archive").unwrap();
        publish(root, &digest).unwrap();
        let url = format!("{}/{ARCHIVE}", host.url);
        let head = client.head(&url).send().await.unwrap();
        assert_eq!(head.headers()["etag"], tag);
        assert_eq!(head.headers()["x-slop-decoded-length"], "7");
        let cached = client
            .get(&url)
            .header("if-none-match", format!("W/{tag}"))
            .send()
            .await
            .unwrap();
        assert_eq!(cached.status(), 304);
    }
    let root = second._root.as_ref().unwrap().path();
    let url = format!("{}/{ARCHIVE}", second.url);
    let asset = root.join(ARCHIVE);
    std::fs::write(root.join("replacement"), b"changed").unwrap();
    std::fs::rename(root.join("replacement"), &asset).unwrap();
    let changed = client
        .get(&url)
        .header("if-none-match", &tag)
        .send()
        .await
        .unwrap();
    assert_eq!(changed.status(), 200);
    assert_ne!(changed.headers()["etag"], tag);
    assert_eq!(changed.bytes().await.unwrap().as_ref(), b"changed");
    let changed_digest = format!("{:x}", Sha256::digest(b"changed"));
    publish(root, &changed_digest).unwrap();
    let fresh = client.head(&url).send().await.unwrap();
    assert_eq!(
        fresh.headers()["etag"],
        format!("\"sha256-{changed_digest}\"")
    );

    let marker = root.join(MARKER);
    let outside = tempfile::NamedTempFile::new().unwrap();
    std::fs::copy(&marker, outside.path()).unwrap();
    std::fs::remove_file(&marker).unwrap();
    std::os::unix::fs::symlink(outside.path(), &marker).unwrap();
    let fallback = client.head(&url).send().await.unwrap();
    assert!(
        fallback.headers()["etag"]
            .to_str()
            .unwrap()
            .starts_with("W/")
    );
    std::fs::remove_file(&marker).unwrap();
    std::fs::write(&marker, "malformed").unwrap();
    assert_eq!(
        client.head(&url).send().await.unwrap().headers()["etag"],
        fallback.headers()["etag"]
    );
}
