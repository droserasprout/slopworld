//! Loopback host coordination. Security, credentials, assets and transports have
//! separate owners; fonts owns discovery and immutable font data.
mod bridge;
mod endpoint;
mod fonts;
mod radio;
mod security;
mod static_files;
#[cfg(test)]
mod tests;
mod websocket;

use crate::fonts::FontService;
use anyhow::Result;
use axum::{
    Router,
    body::Body,
    extract::{Request, State},
    http::StatusCode,
    response::Response,
};
pub use endpoint::ConnectionSource;
use std::{path::PathBuf, sync::Arc};
use tokio::net::TcpListener;

#[derive(Clone, Debug)]
pub struct ServerConfig {
    pub root: PathBuf,
    pub port: u16,
    pub public_origin: Option<String>,
    pub connection: ConnectionSource,
}

struct Host {
    root: PathBuf,
    boundary: security::Boundary,
    connection: ConnectionSource,
    client: reqwest::Client,
    upload_budget: Arc<tokio::sync::Semaphore>,
    fonts: Option<Arc<FontService>>,
}

/// Bind IPv4 loopback, announce the actual URL, then serve until canceled.
pub async fn serve(config: ServerConfig, fonts: Arc<FontService>) -> Result<()> {
    let listener = TcpListener::bind((std::net::Ipv4Addr::LOCALHOST, config.port)).await?;
    let port = listener.local_addr()?.port();
    let router = router(config, port, Some(fonts)).await?;
    println!("http://127.0.0.1:{port}");
    axum::serve(listener, router).await?;
    Ok(())
}

/// Build the same-origin router for an already bound host port.
pub async fn router(
    config: ServerConfig,
    port: u16,
    fonts: Option<Arc<FontService>>,
) -> Result<Router> {
    let boundary = security::Boundary::new(port, config.public_origin)?;
    let root = tokio::fs::canonicalize(config.root).await?;
    // Explicitly disable environment proxies: host credentials must go only to
    // the selected daemon. The managed client owns request deadlines.
    let client = reqwest::Client::builder()
        .redirect(reqwest::redirect::Policy::none())
        .no_proxy()
        .retry(reqwest::retry::never())
        .build()?;
    let host = Arc::new(Host {
        root,
        boundary,
        connection: config.connection,
        client,
        upload_budget: Arc::new(tokio::sync::Semaphore::new(bridge::UPLOAD_BUDGET)),
        fonts,
    });
    Ok(Router::new().fallback(dispatch).with_state(host))
}

async fn dispatch(State(host): State<Arc<Host>>, request: Request) -> Response {
    if !host.boundary.admits_host(request.headers()) {
        return reply(StatusCode::FORBIDDEN, "");
    }
    let path = request.uri().path();
    let mut response = if path == "/host/ws" || request.headers().contains_key("upgrade") {
        websocket::response(&host, request).await
    } else if path.starts_with("/host/media/") {
        radio::response(&host, request).await
    } else if request.uri() == "/host/health" || path.starts_with("/host/api/") {
        bridge::response(&host, request).await
    } else if path == "/host/fonts" || path.starts_with("/host/fonts/") {
        fonts::response(&host, request).await
    } else {
        static_files::response(&host.root, path, request.method(), request.headers()).await
    };
    let headers = response.headers_mut();
    headers.insert(
        "cross-origin-opener-policy",
        axum::http::HeaderValue::from_static("same-origin"),
    );
    headers.insert(
        "cross-origin-embedder-policy",
        axum::http::HeaderValue::from_static("require-corp"),
    );
    headers
        .entry("cache-control")
        .or_insert(axum::http::HeaderValue::from_static("no-store"));
    response
}

fn reply(status: StatusCode, body: &'static str) -> Response {
    let mut response = Response::new(Body::from(body));
    *response.status_mut() = status;
    response
}
