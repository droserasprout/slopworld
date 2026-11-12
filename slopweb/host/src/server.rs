//! Loopback host coordination. Security, credentials, assets and transports have
//! separate owners; fonts owns discovery and immutable font data.
mod bridge;
mod endpoint;
mod fonts;
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

async fn router(
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
    } else if request.uri() == "/host/health" || path.starts_with("/host/api/") {
        bridge::response(&host, request).await
    } else if path == "/host/fonts" || path.starts_with("/host/fonts/") {
        fonts::response(&host, request).await
    } else {
        static_files::response(&host.root, path, request.method()).await
    };
    let headers = response.headers_mut();
    headers.insert("cross-origin-opener-policy", "same-origin".parse().unwrap());
    headers.insert(
        "cross-origin-embedder-policy",
        "require-corp".parse().unwrap(),
    );
    headers
        .entry("cache-control")
        .or_insert("no-store".parse().unwrap());
    response
}

fn reply(status: StatusCode, body: &'static str) -> Response {
    Response::builder()
        .status(status)
        .body(Body::from(body))
        .unwrap()
}
