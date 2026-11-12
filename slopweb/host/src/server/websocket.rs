//! Bounded WebSocket relay. Each direction waits for socket capacity before reading again.
use super::{Host, reply, security::single};
use axum::{
    extract::{
        FromRequestParts, Request,
        ws::{Message, WebSocket, WebSocketUpgrade},
    },
    http::StatusCode,
    response::{IntoResponse, Response},
};
use futures::{SinkExt, StreamExt};
use std::time::Duration;
use tokio::net::TcpStream;
use tokio_tungstenite::{
    MaybeTlsStream, WebSocketStream,
    tungstenite::{self, client::IntoClientRequest},
};

const PROTOCOL: &str = "slopworld.protobuf.v2";
const MAX_MESSAGE: usize = 32 * 1024 * 1024;
type Upstream = WebSocketStream<MaybeTlsStream<TcpStream>>;

pub(super) async fn response(host: &Host, request: Request) -> Response {
    let headers = request.headers();
    let key_valid = single(headers, "sec-websocket-key").is_some_and(|key| {
        key.len() == 24
            && key.ends_with("==")
            && key[..22]
                .bytes()
                .all(|c| c.is_ascii_alphanumeric() || b"+/".contains(&c))
    });
    if request.uri() != "/host/ws"
        || request.method() != "GET"
        || single(headers, "origin") != Some(host.boundary.origin(headers).as_str())
        || single(headers, "sec-websocket-protocol") != Some(PROTOCOL)
        || single(headers, "sec-websocket-version") != Some("13")
        || !key_valid
    {
        return reply(StatusCode::FORBIDDEN, "");
    }
    let (mut parts, _) = request.into_parts();
    let Ok(upgrade) = WebSocketUpgrade::from_request_parts(&mut parts, &()).await else {
        return reply(StatusCode::FORBIDDEN, "");
    };
    let Ok(connection) = host.connection.snapshot().await else {
        return reply(StatusCode::SERVICE_UNAVAILABLE, "");
    };
    let Ok(mut target) = connection.endpoint.join("/ws") else {
        return reply(StatusCode::BAD_GATEWAY, "");
    };
    let scheme = if target.scheme() == "https" {
        "wss"
    } else {
        "ws"
    };
    if target.set_scheme(scheme).is_err() {
        return reply(StatusCode::BAD_GATEWAY, "");
    }
    let Ok(mut upstream_request) = target.as_str().into_client_request() else {
        return reply(StatusCode::BAD_GATEWAY, "");
    };
    upstream_request
        .headers_mut()
        .insert("x-slop-token", connection.token);
    upstream_request
        .headers_mut()
        .insert("sec-websocket-protocol", PROTOCOL.parse().unwrap());
    let limits = tungstenite::protocol::WebSocketConfig {
        max_message_size: Some(MAX_MESSAGE),
        max_frame_size: Some(MAX_MESSAGE),
        max_write_buffer_size: MAX_MESSAGE + 1024 * 1024,
        ..Default::default()
    };
    let connected = tokio::time::timeout(
        Duration::from_secs(5),
        tokio_tungstenite::connect_async_with_config(upstream_request, Some(limits), false),
    )
    .await;
    let Ok(Ok((upstream, response))) = connected else {
        return reply(StatusCode::BAD_GATEWAY, "");
    };
    if single(response.headers(), "sec-websocket-protocol") != Some(PROTOCOL) {
        return reply(StatusCode::BAD_GATEWAY, "");
    }
    upgrade
        .protocols([PROTOCOL])
        .max_message_size(MAX_MESSAGE)
        .max_frame_size(MAX_MESSAGE)
        .max_write_buffer_size(MAX_MESSAGE + 1024 * 1024)
        .on_upgrade(move |browser| relay(browser, upstream))
        .into_response()
}

async fn relay(browser: WebSocket, upstream: Upstream) {
    let (mut browser_out, mut browser_in) = browser.split();
    let (mut upstream_out, mut upstream_in) = upstream.split();
    // Separate pumps preserve full duplex progress under backpressure. After one
    // finishes, allow a bounded close-handshake drain, then drop both sockets.
    let to_daemon = async {
        while let Some(Ok(message)) = browser_in.next().await {
            let mapped = match message {
                Message::Text(value) => tungstenite::Message::Text(value),
                Message::Binary(value) => tungstenite::Message::Binary(value),
                // Each endpoint answers control pings locally. Relaying them as
                // well would send duplicate pongs because tungstenite auto-replies.
                Message::Ping(_) => continue,
                Message::Pong(value) => tungstenite::Message::Pong(value),
                Message::Close(value) => tungstenite::Message::Close(value.map(|value| {
                    tungstenite::protocol::CloseFrame {
                        code: value.code.into(),
                        reason: value.reason,
                    }
                })),
            };
            if upstream_out.send(mapped).await.is_err() {
                break;
            }
        }
    };
    let to_browser = async {
        while let Some(Ok(message)) = upstream_in.next().await {
            let closing = matches!(message, tungstenite::Message::Close(_));
            let mapped = match message {
                tungstenite::Message::Text(value) => Message::Text(value),
                tungstenite::Message::Binary(value) => Message::Binary(value),
                tungstenite::Message::Ping(_) => continue,
                tungstenite::Message::Pong(value) => Message::Pong(value),
                tungstenite::Message::Close(value) => {
                    Message::Close(value.map(|value| axum::extract::ws::CloseFrame {
                        code: value.code.into(),
                        reason: value.reason,
                    }))
                }
                tungstenite::Message::Frame(_) => continue,
            };
            if browser_out.send(mapped).await.is_err() {
                break;
            }
            if closing {
                // Reading again flushes tungstenite's queued upstream close reply.
                let _ = upstream_in.next().await;
                break;
            }
        }
    };
    tokio::pin!(to_daemon, to_browser);
    tokio::select! {
        _ = &mut to_daemon => { let _ = tokio::time::timeout(Duration::from_secs(1), &mut to_browser).await; },
        _ = &mut to_browser => { let _ = tokio::time::timeout(Duration::from_secs(1), &mut to_daemon).await; },
    }
}
