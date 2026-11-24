//! Exercise both production HTTP routers and real station sockets without starting a game.

use super::{app, manager};
use axum::Router;
use reqwest::{Client, Response, StatusCode};
use slopweb::server::{ConnectionSource, ServerConfig};
use std::{path::Path, time::Duration};
use tokio::{
    io::{AsyncBufReadExt, AsyncReadExt, AsyncWriteExt, BufReader},
    net::TcpListener,
    task::JoinHandle,
    time::timeout,
};

const WAIT: Duration = Duration::from_secs(5);

async fn serve(listener: TcpListener, router: Router) -> String {
    let address = listener.local_addr().unwrap();
    tokio::spawn(async move { axum::serve(listener, router).await.unwrap() });
    format!("http://{address}")
}

async fn bind() -> TcpListener {
    TcpListener::bind("127.0.0.1:0").await.unwrap()
}

/// Emit one ICY block, then wait in silence until the daemon closes this exact socket.
async fn station(title: &'static str) -> (String, JoinHandle<()>) {
    let listener = bind().await;
    let address = listener.local_addr().unwrap();
    let closed = tokio::spawn(async move {
        let (socket, _) = listener.accept().await.unwrap();
        let mut socket = BufReader::new(socket);
        let mut headers = String::new();
        loop {
            let start = headers.len();
            assert_ne!(socket.read_line(&mut headers).await.unwrap(), 0);
            if headers.get(start..) == Some("\r\n") {
                break;
            }
        }
        assert!(headers.to_ascii_lowercase().contains("icy-metadata: 1"));
        assert!(!headers.to_ascii_lowercase().contains("x-slop-token"));
        let mut response = b"HTTP/1.1 200 OK\r\nContent-Type: audio/aac\r\nicy-metaint: 4\r\nConnection: close\r\n\r\n".to_vec();
        response.extend_from_slice(b"\xffABC\x02");
        let mut metadata = format!("StreamTitle='{title}';").into_bytes();
        metadata.resize(32, 0);
        response.extend_from_slice(&metadata);
        response.extend_from_slice(b"DEF\0");
        socket.get_mut().write_all(&response).await.unwrap();
        assert_eq!(socket.read(&mut [0; 1]).await.unwrap(), 0);
    });
    (format!("http://{address}"), closed)
}

async fn host(root: &Path, daemon: &str) -> String {
    let listener = bind().await;
    let port = listener.local_addr().unwrap().port();
    let config = ServerConfig {
        root: root.to_owned(),
        port,
        public_origin: None,
        connection: ConnectionSource::Direct {
            url: daemon.to_owned(),
            token: Some("root-secret".into()),
            token_file: None,
        },
    };
    let router = slopweb::server::router(config, port, None).await.unwrap();
    serve(listener, router).await
}

struct LocalPlayback {
    url: String,
    title_url: String,
    body: Response,
}

async fn playback(client: &Client, host: &str, stream: &str) -> LocalPlayback {
    let id = uuid::Uuid::new_v4();
    let url = format!("{host}/host/media/radio?station=test&stream={stream}&listener={id}");
    let mut body = client
        .get(&url)
        .header("sec-fetch-site", "same-origin")
        .send()
        .await
        .unwrap();
    assert_eq!(body.status(), StatusCode::OK);
    assert_eq!(body.headers()["content-type"], "audio/aac");
    assert_eq!(body.headers()["cache-control"], "no-store");
    let mut received = Vec::new();
    while received.len() < 8 {
        received.extend_from_slice(&body.chunk().await.unwrap().unwrap());
    }
    assert_eq!(&received, b"\xffABCDEF\0");
    LocalPlayback {
        url,
        title_url: format!("{host}/host/media/radio/title?listener={id}"),
        body,
    }
}

async fn media_get(client: &Client, url: &str) -> Response {
    client
        .get(url)
        .header("sec-fetch-site", "same-origin")
        .send()
        .await
        .unwrap()
}

#[tokio::test]
async fn radio_relay_streams_independently_and_cancels_silent_station_through_host() {
    let Some(root) = crate::test_support::isolated_with_env(|command, root| {
        command
            .env("SLOPD_CONFIG_ROOT", root)
            .env("SLOPD_RUNTIME", "slopcar");
    }) else {
        return;
    };
    let (one, closed_one) = station("One").await;
    let (two, closed_two) = station("Two").await;
    let directory = root.join("jukebox");
    std::fs::create_dir_all(&directory).unwrap();
    std::fs::write(directory.join("test.toml"), format!(
        "[[stream]]\nrate=64\nkey='low'\nurl='{one}'\n[[stream]]\nrate=128\nkey='high'\nurl='{two}'\n"
    )).unwrap();
    let manager = manager();
    let player = &manager.application.music;
    let shared = player.state().await;
    let daemon = serve(bind().await, app(&manager)).await;
    let host = host(&root, &daemon).await;
    let client = Client::builder().no_proxy().build().unwrap();
    timeout(WAIT, async {
        let one = playback(&client, &host, "low").await;
        let two = playback(&client, &host, "high").await;
        assert_eq!(media_get(&client, &one.url).await.status(), 409);
        let title = media_get(&client, &one.title_url)
            .await
            .json::<serde_json::Value>()
            .await
            .unwrap();
        assert_eq!(title, serde_json::json!({"title": "One"}));
        drop(one.body);
        closed_one.await.unwrap();
        assert_eq!(media_get(&client, &one.title_url).await.status(), 404);
        let title = media_get(&client, &two.title_url)
            .await
            .json::<serde_json::Value>()
            .await
            .unwrap();
        assert_eq!(title, serde_json::json!({"title": "Two"}));
        assert!(!closed_two.is_finished());
        let mut config = manager.config().await;
        config.daemon.token = "new-token".into();
        manager
            .config_authority
            .publish_config(crate::config::transactions::ConfigChange {
                new: config,
                root_token_changed: true,
            })
            .await;
        closed_two.await.unwrap();
        assert_eq!(shared, player.state().await);
    })
    .await
    .expect(
        "idle station socket should close promptly when the listener leaves or daemon shuts down",
    );
}
