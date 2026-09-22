use super::*;
use axum::http::HeaderValue;

#[test]
fn errors_keep_the_status_and_put_the_message_in_json() {
    let (status, Proto(body)) = err(StatusCode::BAD_REQUEST, "bad request body");
    let body = serde_json::to_value(body).unwrap();
    assert_eq!(status, StatusCode::BAD_REQUEST);
    assert_eq!(body, json!({ "error": "bad request body" }));
}

#[test]
fn presented_token_accepts_only_a_valid_header_value() {
    let mut headers = HeaderMap::new();
    assert_eq!(presented_token(&headers), None);

    headers.insert(TOKEN_HEADER, HeaderValue::from_static("secret"));
    assert_eq!(presented_token(&headers).as_deref(), Some("secret"));

    headers.insert(
        TOKEN_HEADER,
        HeaderValue::from_bytes(&[0xff]).expect("opaque header value"),
    );
    assert_eq!(presented_token(&headers), None);
}
