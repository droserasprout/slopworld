//! Protobuf HTTP forwarding with isolated host credentials and no mutation replay.
use super::{Host, reply};
use axum::{
    body::{Body, to_bytes},
    extract::Request,
    http::{Method, StatusCode},
    response::Response,
};

pub(super) async fn response(host: &Host, request: Request) -> Response {
    if !host.boundary.same_origin(request.headers())
        || !matches!(
            *request.method(),
            Method::GET
                | Method::HEAD
                | Method::POST
                | Method::PUT
                | Method::DELETE
                | Method::PATCH
        )
    {
        return reply(StatusCode::FORBIDDEN, "");
    }
    let route = if request.uri() == "/host/health" {
        "/api/health"
    } else {
        request
            .uri()
            .path_and_query()
            .map(|value| value.as_str())
            .unwrap_or_default()
            .strip_prefix("/host")
            .unwrap_or_default()
    };
    let Ok(connection) = host.connection.snapshot().await else {
        return reply(
            StatusCode::SERVICE_UNAVAILABLE,
            "Configure the host daemon endpoint.",
        );
    };
    let Some(target) = target(&connection.endpoint, route) else {
        return reply(StatusCode::BAD_REQUEST, "");
    };
    let method = request.method().clone();
    let Ok(body) = to_bytes(request.into_body(), 32 * 1024 * 1024).await else {
        return reply(StatusCode::PAYLOAD_TOO_LARGE, "");
    };
    let mut upstream = host
        .client
        .request(method.clone(), target)
        .header("x-slop-token", connection.token)
        // Root task and worker operations resolve their caller from this header.
        .header("x-slop-session", "host")
        .header("content-type", "application/x-protobuf");
    if !matches!(method, Method::GET | Method::HEAD) {
        upstream = upstream.body(body);
    }
    let Ok(response) = upstream.send().await else {
        return reply(StatusCode::BAD_GATEWAY, "Host daemon unavailable.");
    };
    // Redirects could carry credentials across authorities; never follow or expose them.
    if response.status().is_redirection() {
        return reply(StatusCode::BAD_GATEWAY, "Host daemon unavailable.");
    }
    let status = response.status();
    let body = if method == Method::HEAD {
        Body::empty()
    } else {
        Body::from_stream(response.bytes_stream())
    };
    Response::builder()
        .status(status)
        .header("content-type", "application/x-protobuf")
        .body(body)
        .unwrap()
}

fn target(endpoint: &url::Url, route: &str) -> Option<url::Url> {
    let (path, query) = route.split_once('?').unwrap_or((route, ""));
    if !path.starts_with("/api/")
        || path.len() <= 5
        || !path
            .bytes()
            .all(|c| c.is_ascii_alphanumeric() || b"_/%.-".contains(&c))
        || query.contains('#')
    {
        return None;
    }
    // Search terms and filenames can contain credential-like text as data.
    percent_encoding::percent_decode_str(query)
        .decode_utf8()
        .ok()?;
    if url::form_urlencoded::parse(query.as_bytes()).any(|(name, _)| {
        ["token", "credential", "authorization"]
            .iter()
            .any(|key| name.eq_ignore_ascii_case(key))
    }) {
        return None;
    }
    let decoded_path = percent_encoding::percent_decode_str(path)
        .decode_utf8()
        .ok()?;
    if decoded_path.contains('\\')
        || decoded_path
            .split('/')
            .any(|part| matches!(part, "." | ".."))
    {
        return None;
    }
    let target = endpoint.join(route.strip_prefix('/')?).ok()?;
    target
        .path()
        .strip_prefix(endpoint.path())?
        .starts_with("api/")
        .then_some(target)
}
