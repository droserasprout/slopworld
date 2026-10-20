use super::parse_kind;
use crate::api::types::SearchReq;
use crate::presets::PresetSource;
pub(super) use crate::test_support::fixture;
use axum::http::StatusCode;

#[test]
fn preset_source_and_kind_errors_are_explicit() {
    assert_eq!(PresetSource::from_presence(true, true).as_str(), "override");
    assert_eq!(PresetSource::from_presence(true, false).as_str(), "system");
    assert_eq!(PresetSource::from_presence(false, true).as_str(), "user");
    assert_eq!(
        PresetSource::from_presence(false, false).as_str(),
        "unknown"
    );
    parse_kind("sandbox_presets").unwrap();
    parse_kind("app_presets").unwrap();
    let (status, body) = parse_kind("other").unwrap_err();
    assert_eq!(status, StatusCode::BAD_REQUEST);
    assert!(body.0.error.contains("unknown preset kind"));
}

#[test]
fn search_filters_gitignored_files_by_default() {
    let missing: SearchReq = serde_json::from_value(serde_json::json!({
        "path": "/tmp/project",
        "q": "needle"
    }))
    .unwrap();
    assert!(!missing.gitignore);

    let respect: SearchReq = serde_json::from_value(serde_json::json!({
        "path": "/tmp/project",
        "q": "needle",
        "gitignore": "1"
    }))
    .unwrap();
    assert!(respect.gitignore);

    let include: SearchReq = serde_json::from_value(serde_json::json!({
        "path": "/tmp/project",
        "q": "needle",
        "gitignore": "0"
    }))
    .unwrap();
    assert!(!include.gitignore);
}
