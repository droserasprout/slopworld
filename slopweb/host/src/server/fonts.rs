//! HTTP admission and caching for font metadata and immutable content-addressed bytes.
//! The font service owns validation, discovery, and private cache paths.
use super::{Host, reply, security::single};
use axum::{
    body::{Body, to_bytes},
    extract::Request,
    http::{Method, StatusCode},
    response::Response,
};

pub(super) async fn response(host: &Host, request: Request) -> Response {
    if !host.boundary.same_origin(request.headers()) {
        return reply(StatusCode::FORBIDDEN, "");
    }
    let Some(fonts) = &host.fonts else {
        return reply(StatusCode::NOT_FOUND, "");
    };
    let path = request.uri().path();
    if path == "/host/fonts" {
        if request.method() != Method::POST {
            return reply(StatusCode::FORBIDDEN, "");
        }
        let Ok(body) = to_bytes(request.into_body(), 512 * 1024).await else {
            return unavailable();
        };
        let Ok(value) = serde_json::from_slice(&body) else {
            return unavailable();
        };
        return json(fonts.request(value).await, false);
    }
    if !matches!(*request.method(), Method::GET | Method::HEAD) {
        return reply(StatusCode::FORBIDDEN, "");
    }
    let head = *request.method() == Method::HEAD;
    if path == "/host/fonts/catalog" {
        let family =
            url::form_urlencoded::parse(request.uri().query().unwrap_or_default().as_bytes())
                .find(|(key, _)| key == "family")
                .map(|(_, value)| value.into_owned());
        let result = match family {
            Some(family) => fonts.catalog_for(&family).await,
            None => fonts.catalog().await,
        };
        return json(result, head);
    }
    let Some(hash) = path.strip_prefix("/host/fonts/files/") else {
        return reply(StatusCode::NOT_FOUND, "");
    };
    if hash.len() != 64
        || !hash
            .bytes()
            .all(|c| c.is_ascii_digit() || (b'a'..=b'f').contains(&c))
    {
        return reply(StatusCode::NOT_FOUND, "");
    }
    let Ok(bytes) = fonts.font_bytes(hash).await else {
        return reply(StatusCode::NOT_FOUND, "");
    };
    immutable_file(hash, bytes, &request)
}

fn immutable_file(hash: &str, bytes: Vec<u8>, request: &Request) -> Response {
    let head = request.method() == Method::HEAD;
    let etag = format!("\"{hash}\"");
    let unchanged = single(request.headers(), "if-none-match").is_some_and(|value| {
        value
            .split(',')
            .any(|part| matches!(part.trim(), "*") || part.trim().trim_start_matches("W/") == etag)
    });
    let length = bytes.len();
    let body = if unchanged || head {
        Body::empty()
    } else {
        Body::from(bytes)
    };
    let mut response = Response::builder()
        .status(if unchanged {
            StatusCode::NOT_MODIFIED
        } else {
            StatusCode::OK
        })
        .header("content-type", "font/ttf")
        .header("cache-control", "private, max-age=31536000, immutable")
        .header("cross-origin-resource-policy", "same-origin")
        .header("etag", etag);
    if !unchanged {
        response = response.header("content-length", length);
    }
    response.body(body).unwrap()
}

fn json(result: anyhow::Result<serde_json::Value>, head: bool) -> Response {
    let Ok(value) = result else {
        return unavailable();
    };
    let Ok(bytes) = serde_json::to_vec(&value) else {
        return unavailable();
    };
    if bytes.len() > 64 * 1024 * 1024 {
        return unavailable();
    }
    Response::builder()
        .header("content-type", "application/json")
        .header("content-length", bytes.len())
        .body(if head {
            Body::empty()
        } else {
            Body::from(bytes)
        })
        .unwrap()
}
fn unavailable() -> Response {
    reply(
        StatusCode::UNPROCESSABLE_ENTITY,
        "Font preparation unavailable",
    )
}

#[cfg(test)]
#[path = "fonts_tests.rs"]
mod tests;
