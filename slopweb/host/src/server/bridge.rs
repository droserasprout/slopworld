//! Protobuf HTTP forwarding with isolated host credentials and no mutation replay.
use super::{Host, reply};
use axum::{
    body::{Body, HttpBody, to_bytes},
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
    let body = match buffered_upload(&host.upload_budget, request.into_body()).await {
        Ok(body) => body,
        Err(status) => return reply(status, ""),
    };
    let mut upstream = host
        .client
        .request(method.clone(), target)
        // Root task and worker operations resolve their caller from this header.
        .header("x-slop-session", "host")
        .header("content-type", "application/x-protobuf");
    if !matches!(method, Method::GET | Method::HEAD) {
        upstream = upstream.body(body);
    }
    let response = match send(upstream, connection.token).await {
        Ok(response) => response,
        Err(status) => return reply(status, "Host daemon unavailable."),
    };
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
        .unwrap_or_else(|_error| reply(StatusCode::INTERNAL_SERVER_ERROR, ""))
}

// Both HTTP bridges use the same credential and no-redirect boundary. Routing,
// upload admission and downstream response policy remain with their handlers.
pub(super) async fn send(
    request: reqwest::RequestBuilder,
    token: axum::http::HeaderValue,
) -> Result<reqwest::Response, StatusCode> {
    let response = request
        .header("x-slop-token", token)
        .send()
        .await
        .map_err(|_transport| StatusCode::BAD_GATEWAY)?;
    if response.status().is_redirection() {
        return Err(StatusCode::BAD_GATEWAY);
    }
    Ok(response)
}

pub(super) const UPLOAD_BUDGET: usize = 64 * 1024 * 1024;

struct BufferedUpload {
    body: axum::body::Bytes,
    _reservation: Option<tokio::sync::OwnedSemaphorePermit>,
}

impl AsRef<[u8]> for BufferedUpload {
    fn as_ref(&self) -> &[u8] {
        &self.body
    }
}

async fn buffered_upload(
    budget_pool: &std::sync::Arc<tokio::sync::Semaphore>,
    body: Body,
) -> Result<axum::body::Bytes, StatusCode> {
    const LIMIT: u32 = 32 * 1024 * 1024;
    if body.size_hint().lower() > u64::from(LIMIT) {
        return Err(StatusCode::PAYLOAD_TOO_LARGE);
    }
    // Reserve before reading so concurrent chunked requests cannot each grow unchecked.
    let budget = u32::try_from(body.size_hint().upper().unwrap_or(u64::MAX))
        .unwrap_or(LIMIT)
        .min(LIMIT);
    let reservation = if budget == 0 {
        None
    } else {
        Some(
            budget_pool
                .clone()
                .acquire_many_owned(budget)
                .await
                .map_err(|_closed| StatusCode::SERVICE_UNAVAILABLE)?,
        )
    };
    let Ok(body) = to_bytes(body, budget as usize).await else {
        return Err(StatusCode::PAYLOAD_TOO_LARGE);
    };
    Ok(if body.is_empty() {
        body
    } else {
        axum::body::Bytes::from_owner(BufferedUpload {
            body,
            _reservation: reservation,
        })
    })
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

#[cfg(test)]
mod upload_tests {
    use super::*;
    use futures::FutureExt;
    use std::sync::Arc;
    use tokio::sync::Semaphore;

    #[tokio::test]
    async fn concurrent_uploads_hold_budget_until_last_body_owner_drops() {
        let pool = Arc::new(Semaphore::new(UPLOAD_BUDGET));
        let mut requests: Vec<_> = (0..8)
            .map(|_| {
                let body = Body::from_stream(futures::stream::once(async {
                    Ok::<_, std::io::Error>(vec![1; UPLOAD_BUDGET / 2])
                }));
                Box::pin(buffered_upload(&pool, body))
            })
            .collect();
        let mut active = Vec::new();
        for request in &mut requests {
            if let Some(result) = request.as_mut().now_or_never() {
                active.push(result.unwrap());
            }
        }
        assert_eq!(active.len(), 2);
        assert_eq!(active.iter().map(|b| b.len()).sum::<usize>(), UPLOAD_BUDGET);
        let copy = active.pop().unwrap();
        let retained = copy.clone();
        drop(copy);
        assert_eq!(pool.available_permits(), 0);
        drop(retained);
        for request in requests.into_iter().skip(2) {
            drop(request.await.unwrap());
        }
        drop(active);
        assert_eq!(pool.available_permits(), UPLOAD_BUDGET);
    }
}
