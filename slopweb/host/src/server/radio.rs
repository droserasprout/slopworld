//! Same-origin radio proxy. Only the two media routes receive host credentials.
//! The downstream body owns the daemon response so disconnects cancel silent reads too.

use super::{Host, reply};
use axum::{
    body::Body,
    extract::Request,
    http::{Method, StatusCode, header},
    response::Response,
};

pub(super) async fn response(host: &Host, request: Request) -> Response {
    if !host.boundary.same_origin(request.headers()) {
        return reply(StatusCode::FORBIDDEN, "");
    }
    if request.method() != Method::GET {
        return reply(StatusCode::METHOD_NOT_ALLOWED, "");
    }
    let route = match request.uri().path() {
        "/host/media/radio" => "media/radio",
        "/host/media/radio/title" => "media/radio/title",
        _ => return reply(StatusCode::NOT_FOUND, ""),
    };
    let Ok(connection) = host.connection.snapshot().await else {
        return reply(
            StatusCode::SERVICE_UNAVAILABLE,
            "Configure the host daemon endpoint.",
        );
    };
    let Ok(mut target) = connection.endpoint.join(route) else {
        return reply(StatusCode::BAD_REQUEST, "");
    };
    target.set_query(request.uri().query());
    let Ok(upstream) = host
        .client
        .get(target)
        .header("x-slop-token", connection.token)
        .send()
        .await
    else {
        return reply(StatusCode::BAD_GATEWAY, "Host daemon unavailable.");
    };
    if upstream.status().is_redirection() {
        return reply(StatusCode::BAD_GATEWAY, "Host daemon unavailable.");
    }
    if !upstream.status().is_success() {
        return reply(upstream.status(), "");
    }
    let Some(content_type) = upstream.headers().get(header::CONTENT_TYPE).cloned() else {
        return reply(StatusCode::BAD_GATEWAY, "");
    };
    Response::builder()
        .status(upstream.status())
        .header(header::CONTENT_TYPE, content_type)
        .header(header::X_CONTENT_TYPE_OPTIONS, "nosniff")
        .header(
            header::CONTENT_SECURITY_POLICY,
            "sandbox; default-src 'none'",
        )
        .body(Body::from_stream(upstream.bytes_stream()))
        .unwrap_or_else(|_| reply(StatusCode::INTERNAL_SERVER_ERROR, ""))
}
