//! Bounded WebSocket relay. Each direction waits for socket capacity before reading again.
use super::{
    Host,
    audio_exit::{Registration, Scope},
    reply,
    security::single,
};
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
// Canonical ClientMessage { audio { stop {} } } in the v2 wire schema.
const AUDIO_STOP: [u8; 4] = [0x52, 2, 0x12, 0];
const MAX_MESSAGE: usize = 32 * 1024 * 1024;
type Upstream = WebSocketStream<MaybeTlsStream<TcpStream>>;

pub(super) async fn response(host: &Host, request: Request) -> Response {
    let headers = request.headers();
    let key_valid = single(headers, "sec-websocket-key").is_some_and(|key| {
        key.len() == 24
            && key.ends_with("==")
            && key
                .bytes()
                .take(22)
                .all(|c| c.is_ascii_alphanumeric() || b"+/".contains(&c))
    });
    if request.uri().path() != "/host/ws"
        || request.method() != "GET"
        || single(headers, "origin") != Some(host.boundary.origin(headers).as_str())
        || single(headers, "sec-websocket-protocol") != Some(PROTOCOL)
        || single(headers, "sec-websocket-version") != Some("13")
        || !key_valid
    {
        return reply(StatusCode::FORBIDDEN, "");
    }
    let query = request.uri().query();
    let scope = query.and_then(Scope::parse);
    if query.is_some() && scope.is_none() {
        return reply(StatusCode::BAD_REQUEST, "");
    }
    let (mut parts, _) = request.into_parts();
    let Ok(upgrade) = WebSocketUpgrade::from_request_parts(&mut parts, &()).await else {
        return reply(StatusCode::FORBIDDEN, "");
    };
    let Ok(connection) = host.connection.snapshot().await else {
        return reply(StatusCode::SERVICE_UNAVAILABLE, "");
    };
    let Ok(mut target) = connection.endpoint.join("ws") else {
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
    upstream_request.headers_mut().insert(
        "sec-websocket-protocol",
        axum::http::HeaderValue::from_static(PROTOCOL),
    );
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
    let scoped = scope.is_some();
    let exit = scope.and_then(|scope| host.audio_exits.register(scope));
    if scoped && exit.is_none() {
        return reply(StatusCode::CONFLICT, "");
    }
    upgrade
        .protocols([PROTOCOL])
        .max_message_size(MAX_MESSAGE)
        .max_frame_size(MAX_MESSAGE)
        .max_write_buffer_size(MAX_MESSAGE + 1024 * 1024)
        .on_upgrade(move |browser| relay(browser, upstream, exit))
        .into_response()
}

// The beacon and the browser close travel independently. Hold only the original
// upstream connection briefly if close wins the race.
const BEACON_GRACE: Duration = Duration::from_secs(3);

async fn exit_requested(exit: &mut Option<Registration>) -> bool {
    match exit {
        Some(exit) => exit.requested().await,
        None => std::future::pending().await,
    }
}

async fn forward_browser(
    mut browser: futures::stream::SplitStream<WebSocket>,
    mut upstream: futures::stream::SplitSink<Upstream, tungstenite::Message>,
    mut exit: Option<Registration>,
) {
    let mut close = None;
    let stop = loop {
        let message = tokio::select! {
            biased;
            stop = exit_requested(&mut exit) => break stop,
            message = browser.next() => message,
        };
        let mapped = match message {
            Some(Ok(Message::Text(value))) => tungstenite::Message::Text(value),
            Some(Ok(Message::Binary(value))) => tungstenite::Message::Binary(value),
            // Each endpoint answers pings locally. Relaying them duplicates pongs.
            Some(Ok(Message::Ping(_))) => continue,
            Some(Ok(Message::Pong(value))) => tungstenite::Message::Pong(value),
            end => {
                if let Some(Ok(Message::Close(frame))) = end {
                    close = frame;
                }
                break if let Some(exit) = &mut exit {
                    tokio::time::timeout(BEACON_GRACE, exit.requested())
                        .await
                        .unwrap_or(false)
                } else {
                    false
                };
            }
        };
        if upstream.send(mapped).await.is_err() {
            return;
        }
    };
    if stop
        && upstream
            .send(tungstenite::Message::Binary(AUDIO_STOP.to_vec()))
            .await
            .is_err()
    {
        return;
    }
    let frame = close.map(|frame| tungstenite::protocol::CloseFrame {
        code: frame.code.into(),
        reason: frame.reason,
    });
    drop(upstream.send(tungstenite::Message::Close(frame)).await);
}

async fn drain_until_close(upstream: &mut futures::stream::SplitStream<Upstream>) {
    while let Some(Ok(message)) = upstream.next().await {
        if message.is_close() {
            break;
        }
    }
}

async fn relay(browser: WebSocket, upstream: Upstream, exit: Option<Registration>) {
    let (mut browser_out, browser_in) = browser.split();
    let (upstream_out, mut upstream_in) = upstream.split();
    // Separate pumps preserve full duplex progress under backpressure. After one
    // finishes, allow a bounded close-handshake drain, then drop both sockets.
    let scoped = exit.is_some();
    let close_wait = if scoped {
        Duration::from_secs(10)
    } else {
        Duration::from_secs(1)
    };
    let to_daemon = forward_browser(browser_in, upstream_out, exit);
    let to_browser = async {
        while let Some(Ok(message)) = upstream_in.next().await {
            let closing = matches!(message, tungstenite::Message::Close(_));
            let mapped = match message {
                tungstenite::Message::Text(value) => Message::Text(value),
                tungstenite::Message::Binary(value) => Message::Binary(value),
                tungstenite::Message::Ping(_) | tungstenite::Message::Frame(_) => continue,
                tungstenite::Message::Pong(value) => Message::Pong(value),
                tungstenite::Message::Close(value) => {
                    Message::Close(value.map(|value| axum::extract::ws::CloseFrame {
                        code: value.code.into(),
                        reason: value.reason,
                    }))
                }
            };
            if browser_out.send(mapped).await.is_err() {
                if scoped && !closing {
                    // The page can vanish first. Keep reading daemon events until it
                    // processes the final stop and the close frame that follows it.
                    let _drained =
                        tokio::time::timeout(close_wait, drain_until_close(&mut upstream_in)).await;
                }
                break;
            }
            if closing {
                // Reading again flushes tungstenite's queued upstream close reply.
                drop(upstream_in.next().await);
                break;
            }
        }
    };
    tokio::pin!(to_daemon, to_browser);
    tokio::select! {
        () = &mut to_daemon => { let _drained = tokio::time::timeout(close_wait, &mut to_browser).await; },
        () = &mut to_browser => { let _drained = tokio::time::timeout(Duration::from_secs(1), &mut to_daemon).await; },
    }
}
