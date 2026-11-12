//! Immutable delivery headers survive cache revalidation and HEAD without returning bytes.
use super::*;

#[tokio::test]
async fn immutable_files_support_conditional_get_and_head() {
    let hash = "a".repeat(64);
    let etag = format!("\"{hash}\"");
    for (method, condition, expected) in [
        (Method::GET, None, StatusCode::OK),
        (Method::HEAD, None, StatusCode::OK),
        (Method::GET, Some(etag.clone()), StatusCode::NOT_MODIFIED),
        (
            Method::GET,
            Some(format!("W/{etag}")),
            StatusCode::NOT_MODIFIED,
        ),
        (Method::GET, Some("*".into()), StatusCode::NOT_MODIFIED),
        (Method::GET, Some("\"other\"".into()), StatusCode::OK),
    ] {
        let mut builder = Request::builder().method(method.clone());
        if let Some(condition) = condition {
            builder = builder.header("if-none-match", condition);
        }
        let response = immutable_file(
            &hash,
            b"font bytes".to_vec(),
            &builder.body(Body::empty()).unwrap(),
        );
        assert_eq!(response.status(), expected);
        assert_eq!(response.headers()["etag"], etag);
        assert_eq!(
            response.headers()["cache-control"],
            "private, max-age=31536000, immutable"
        );
        assert_eq!(
            response.headers()["cross-origin-resource-policy"],
            "same-origin"
        );
        assert_eq!(response.headers()["content-type"], "font/ttf");
        if expected == StatusCode::OK {
            assert_eq!(response.headers()["content-length"], "10");
        }
        let bytes = to_bytes(response.into_body(), 1024).await.unwrap();
        assert_eq!(
            bytes.as_ref(),
            if expected == StatusCode::NOT_MODIFIED || method == Method::HEAD {
                b"".as_slice()
            } else {
                b"font bytes"
            }
        );
    }
}
