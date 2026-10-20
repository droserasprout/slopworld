use super::*;
use crate::files::search::{SEARCH_LIMIT, SEARCH_RECORD_LIMIT, SEARCH_TEXT_LIMIT};
use serde_json::json;
use std::path::Path;

#[tokio::test]
async fn include_filters_preserve_ignore_and_hidden_policy_before_limiting_results() {
    let fixture = crate::api::handlers::tests::fixture("search-include-ignore");
    std::fs::create_dir_all(fixture.join(".git")).unwrap();
    std::fs::write(fixture.join(".gitignore"), "ignored.rs\n").unwrap();
    for path in ["keep.rs", "ignored.rs", ".hidden.rs", "other.txt"] {
        std::fs::write(fixture.join(path), "needle\n").unwrap();
    }
    let manager = crate::application::test_application(crate::config::Config::default());
    for (gitignore, hidden, expected) in [
        (true, false, vec!["keep.rs"]),
        (false, false, vec!["ignored.rs", "keep.rs"]),
        (true, true, vec![".hidden.rs", "keep.rs"]),
    ] {
        let mut request = search_request(&fixture, "needle", expected.len());
        request.include = "*.rs".into();
        request.gitignore = gitignore;
        request.hidden = hidden;
        let result = search(State(manager.clone()), Query(request))
            .await
            .unwrap()
            .0;
        let mut paths: Vec<_> = result.matches.iter().map(|row| row.path.as_str()).collect();
        paths.sort_unstable();
        assert_eq!(paths, expected);
        assert!(!result.truncated);
    }
}

#[tokio::test]
async fn include_filter_skips_oversized_unselected_matches_and_validates_globs() {
    let fixture = crate::api::handlers::tests::fixture("search-include-large");
    std::fs::write(
        fixture.join("large.txt"),
        "needle".repeat(SEARCH_RECORD_LIMIT),
    )
    .unwrap();
    std::fs::write(fixture.join("keep.rs"), "needle\n").unwrap();
    let manager = crate::application::test_application(crate::config::Config::default());
    let mut request = search_request(&fixture, "needle", 1);
    request.include = "*.{cs,rs}".into();
    let result = search(State(manager.clone()), Query(request))
        .await
        .unwrap()
        .0;
    assert_eq!(result.matches.len(), 1);
    assert_eq!(result.matches[0].path, "keep.rs");
    assert!(!result.truncated);

    let mut invalid = search_request(&fixture, "needle", 1);
    invalid.include = "[".into();
    let (status, _) = search(State(manager), Query(invalid)).await.unwrap_err();
    assert_eq!(status, StatusCode::BAD_REQUEST);
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
    let manager = crate::application::test_application(crate::config::Config::default());
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
    let manager = crate::application::test_application(crate::config::Config::default());
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
    let manager = crate::application::test_application(crate::config::Config::default());
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
async fn search_default_and_maximum_limits_preserve_exact_truncation() {
    let fixture = crate::api::handlers::tests::fixture("search-result-limit");
    let manager = crate::application::test_application(crate::config::Config::default());
    for (lines, requested_limit, truncated) in [
        (SEARCH_LIMIT, None, false),
        (SEARCH_LIMIT + 1, None, true),
        (SEARCH_LIMIT + 1, Some(usize::MAX), true),
    ] {
        std::fs::write(fixture.join("matches.txt"), "needle\n".repeat(lines)).unwrap();
        let mut request = search_request(&fixture, "needle", SEARCH_LIMIT);
        request.limit = requested_limit;
        let result = search(State(manager.clone()), Query(request))
            .await
            .unwrap()
            .0;
        assert_eq!(result.matches.len(), SEARCH_LIMIT);
        assert_eq!(result.truncated, truncated);
        assert_eq!(result.matches.last().unwrap().line, SEARCH_LIMIT as u64);
    }
}

#[tokio::test]
async fn search_distinguishes_invalid_requests_from_no_matches() {
    let fixture = crate::api::handlers::tests::fixture("search");
    let manager = crate::application::test_application(crate::config::Config::default());
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
async fn byte_encoded_lines_preserve_matches_and_source_columns() {
    let fixture = crate::api::handlers::tests::fixture("search-byte-lines");
    let manager = crate::application::test_application(crate::config::Config::default());
    for prefix in [b"caf\xe9 ".to_vec(), vec![0xff; 2000]] {
        let mut bytes = prefix.clone();
        bytes.extend_from_slice(b"needle\n");
        std::fs::write(fixture.join("legacy.txt"), bytes).unwrap();
        let result = search(
            State(manager.clone()),
            Query(search_request(&fixture, "needle", 10)),
        )
        .await
        .unwrap()
        .0;
        assert_eq!(result.matches.len(), 1);
        let matched = &result.matches[0];
        assert_eq!(matched.path, "legacy.txt");
        assert_eq!(matched.line, 1);
        assert_eq!(matched.column, prefix.len() as u64 + 1);
        assert!(matched.text.contains("needle"));
        assert!(matched.text.contains('\u{fffd}'));
        assert!(matched.text.len() <= SEARCH_TEXT_LIMIT + 6);
        assert!(!result.truncated);
    }
}

#[tokio::test]
async fn unsupported_search_names_return_an_explicit_error() {
    use std::os::unix::ffi::OsStrExt;
    let fixture = crate::api::handlers::tests::fixture("search-invalid-name");
    std::fs::write(
        fixture.join(std::ffi::OsStr::from_bytes(b"bad\xff.txt")),
        "needle\n",
    )
    .unwrap();
    let manager = crate::application::test_application(crate::config::Config::default());
    let (status, Proto(error)) = search(
        State(manager),
        Query(search_request(&fixture, "needle", 10)),
    )
    .await
    .unwrap_err();
    assert_eq!(status, StatusCode::BAD_REQUEST);
    assert!(error.error.contains("not valid UTF-8"));
}

fn search_request(path: &Path, query: &str, limit: usize) -> SearchReq {
    serde_json::from_value(json!({
        "path": path, "q": query, "limit": limit,
    }))
    .unwrap()
}
