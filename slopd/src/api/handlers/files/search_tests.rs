use super::*;
use serde_json::json;
use std::path::Path;

fn search_request(path: &Path, query: &str, limit: usize) -> SearchReq {
    serde_json::from_value(json!({
        "path": path, "q": query, "limit": limit,
    }))
    .unwrap()
}

#[tokio::test]
async fn search_filters_paths_with_exclusions_taking_precedence() {
    let fixture = crate::api::handlers::tests::fixture("search-paths");
    std::fs::create_dir_all(fixture.join("nested")).unwrap();
    std::fs::create_dir_all(fixture.join(".git")).unwrap();
    for path in [
        "keep.rs",
        "skip.rs",
        "other.txt",
        "nested/keep.rs",
        ".git/keep.rs",
    ] {
        std::fs::write(fixture.join(path), "needle\n").unwrap();
    }
    let manager = crate::session::test_manager(crate::config::Config::default());
    for (include, exclude, expected) in [
        ("*.rs", "skip.rs", vec!["keep.rs", "nested/keep.rs"]),
        ("", "*.rs", vec!["other.txt"]),
        ("nested/**", "", vec!["nested/keep.rs"]),
        ("*.rs", "*.rs", vec![]),
    ] {
        let mut request = search_request(&fixture, "needle", 200);
        request.include = include.to_owned();
        request.exclude = exclude.to_owned();
        request.hidden = true;
        let result = search(State(manager.clone()), Query(request))
            .await
            .unwrap()
            .0;
        let mut paths: Vec<_> = result.matches.iter().map(|row| row.path.as_str()).collect();
        paths.sort_unstable();
        assert_eq!(paths, expected, "include={include}, exclude={exclude}");
        assert!(!result.truncated);
    }
}

#[tokio::test]
async fn search_literal_directory_filters_include_descendants() {
    let fixture = crate::api::handlers::tests::fixture("search-directory-paths");
    for path in [
        "notes/guide.md",
        "notes/deep/code.md",
        "nested/notes/guide.md",
        "other.md",
    ] {
        let file = fixture.join(path);
        std::fs::create_dir_all(file.parent().unwrap()).unwrap();
        std::fs::write(file, "code\n").unwrap();
    }
    let manager = crate::session::test_manager(crate::config::Config::default());
    for (include, exclude, expected) in [
        (
            "notes",
            "",
            vec![
                "nested/notes/guide.md",
                "notes/deep/code.md",
                "notes/guide.md",
            ],
        ),
        (
            "notes/",
            "notes/deep",
            vec!["nested/notes/guide.md", "notes/guide.md"],
        ),
        ("nested/notes", "", vec!["nested/notes/guide.md"]),
        ("", "notes", vec!["other.md"]),
    ] {
        let mut request = search_request(&fixture, "code", 200);
        request.include = include.to_owned();
        request.exclude = exclude.to_owned();
        let result = search(State(manager.clone()), Query(request))
            .await
            .unwrap()
            .0;
        let mut paths: Vec<_> = result.matches.iter().map(|row| row.path.as_str()).collect();
        paths.sort_unstable();
        assert_eq!(paths, expected, "include={include}, exclude={exclude}");
    }
}

#[tokio::test]
async fn search_reports_relative_paths_byte_columns_and_real_truncation() {
    let fixture = crate::api::handlers::tests::fixture("search");
    std::fs::write(fixture.join("café.txt"), "é needle\nneedle again\n").unwrap();
    let manager = crate::session::test_manager(crate::config::Config::default());
    let result = search(
        State(manager.clone()),
        Query(search_request(&fixture, "needle", 2)),
    )
    .await
    .unwrap()
    .0;
    assert!(!result.truncated);
    assert_eq!(result.matches.len(), 2);
    assert_eq!(result.matches[0].path, "café.txt");
    assert_eq!(result.matches[0].line, 1);
    assert_eq!(result.matches[0].column, 4);
    assert_eq!(result.matches[0].text, "é needle");
    assert_eq!(result.matches[1].line, 2);
    for limit in [0, 1] {
        let result = search(
            State(manager.clone()),
            Query(search_request(&fixture, "needle", limit)),
        )
        .await
        .unwrap()
        .0;
        assert!(result.truncated);
        assert_eq!(result.matches.len(), 1);
    }
    let result = search(State(manager), Query(search_request(&fixture, "absent", 2)))
        .await
        .unwrap()
        .0;
    assert!(result.matches.is_empty());
    assert!(!result.truncated);
}

#[tokio::test]
async fn search_distinguishes_invalid_requests_from_no_matches() {
    let fixture = crate::api::handlers::tests::fixture("search");
    let manager = crate::session::test_manager(crate::config::Config::default());
    for (request, expected) in [
        (
            search_request(Path::new(""), "query", 1),
            "A path is required.",
        ),
        (
            search_request(&fixture, "", 1),
            "A search query is required.",
        ),
        (
            search_request(&fixture.join("missing"), "query", 1),
            "The daemon could not start ripgrep",
        ),
    ] {
        let (status, Proto(error)) = search(State(manager.clone()), Query(request))
            .await
            .unwrap_err();
        assert_eq!(status, StatusCode::BAD_REQUEST);
        assert!(error.error.contains(expected), "{}", error.error);
    }
    let mut request = search_request(&fixture, "[", 1);
    request.regex = true;
    let (status, Proto(error)) = search(State(manager), Query(request)).await.unwrap_err();
    assert_eq!(status, StatusCode::BAD_REQUEST);
    assert!(
        error.error.contains("Ripgrep exited with status"),
        "{}",
        error.error
    );
}

#[tokio::test]
async fn search_record_reader_stops_at_its_byte_limit() {
    let mut exact = tokio::io::BufReader::new(&b"1234\n"[..]);
    assert!(matches!(
        super::read_search_record(&mut exact, 5).await.unwrap(),
        Some(super::SearchRecord::Line(record)) if record.as_slice() == b"1234\n"
    ));

    let mut oversized = tokio::io::BufReader::new(&b"12345\n"[..]);
    assert!(matches!(
        super::read_search_record(&mut oversized, 5).await.unwrap(),
        Some(super::SearchRecord::TooLarge)
    ));

    let mut unterminated = tokio::io::BufReader::new(&b"12345"[..]);
    assert!(matches!(
        super::read_search_record(&mut unterminated, 4)
            .await
            .unwrap(),
        Some(super::SearchRecord::TooLarge)
    ));
}
