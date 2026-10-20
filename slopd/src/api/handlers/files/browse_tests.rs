use super::*;
use crate::files::browse::BROWSE_LIMIT;
use prost::Message;

#[tokio::test]
async fn root_listing_round_trips_without_a_parent() {
    let manager = crate::application::test_application(crate::config::Config::default());
    let request = serde_json::from_value(serde_json::json!({"path": "/", "limit": 1})).unwrap();
    let Proto(reply) = browse(State(manager), Query(request)).await.unwrap();
    let decoded = wire::BrowseResult::decode(reply.encode_to_vec().as_slice()).unwrap();
    assert_eq!(decoded.path, "/");
    assert_eq!(decoded.parent, None);
}

#[tokio::test]
async fn listing_preserves_names_parent_and_truncation_on_the_wire() {
    let fixture = crate::api::handlers::tests::fixture("browse-wire");
    std::fs::create_dir(fixture.join("empty")).unwrap();
    std::fs::write(fixture.join("café.txt"), "text").unwrap();
    let manager = crate::application::test_application(crate::config::Config::default());
    for limit in [1, 10] {
        let request = serde_json::from_value(serde_json::json!({
            "path": fixture.to_str().unwrap(), "files": "1", "limit": limit
        }))
        .unwrap();
        let Proto(reply) = browse(State(manager.clone()), Query(request))
            .await
            .unwrap();
        let reply = wire::BrowseResult::decode(reply.encode_to_vec().as_slice()).unwrap();
        assert_eq!(reply.parent.as_deref(), fixture.parent().unwrap().to_str());
        assert_eq!(reply.truncated, limit == 1);
        if limit == 10 {
            assert_eq!(reply.dirs, ["empty"]);
            assert_eq!(reply.empty_dirs, ["empty"]);
            assert_eq!(reply.files, ["café.txt"]);
        }
    }
}

#[tokio::test]
async fn unsupported_names_fail_instead_of_aliasing_another_file() {
    use std::os::unix::ffi::OsStrExt;
    let fixture = crate::api::handlers::tests::fixture("browse-invalid-name");
    let original = fixture.join(std::ffi::OsStr::from_bytes(b"bad\xff.txt"));
    std::fs::write(&original, "original").unwrap();
    std::fs::write(fixture.join("bad\u{fffd}.txt"), "other").unwrap();
    let manager = crate::application::test_application(crate::config::Config::default());
    let request = serde_json::from_value(serde_json::json!({
        "path": fixture.to_str().unwrap(), "files": "1"
    }))
    .unwrap();
    let (status, Proto(error)) = browse(State(manager), Query(request)).await.unwrap_err();
    assert_eq!(status, StatusCode::BAD_REQUEST);
    assert!(error.error.contains("not valid UTF-8"));
    assert_eq!(std::fs::read(original).unwrap(), b"original");
    assert_eq!(
        std::fs::read(fixture.join("bad\u{fffd}.txt")).unwrap(),
        b"other"
    );
}

#[tokio::test]
async fn ignored_batches_do_not_consume_visible_limit_or_cause_false_truncation() {
    let fixture = crate::api::handlers::tests::fixture("browse-ignore-batches");
    assert!(
        std::process::Command::new("git")
            .args(["init", "--quiet"])
            .arg(&*fixture)
            .status()
            .unwrap()
            .success()
    );
    std::fs::write(fixture.join(".gitignore"), "ignored-*\n").unwrap();
    for index in 0..BROWSE_LIMIT * 2 + 1 {
        std::fs::write(fixture.join(format!("ignored-{index}")), "text").unwrap();
    }
    std::fs::create_dir(fixture.join("visible-dir")).unwrap();
    std::fs::write(fixture.join("visible.txt"), "text").unwrap();
    let manager = crate::application::test_application(crate::config::Config::default());
    for (limit, count, truncated) in [(1, 1, true), (2, 2, false), (500, 2, false)] {
        let request = serde_json::from_value(serde_json::json!({
            "path": fixture.to_str().unwrap(), "files": "1", "gitignore": "1", "limit": limit
        }))
        .unwrap();
        let Proto(reply) = browse(State(manager.clone()), Query(request))
            .await
            .unwrap();
        assert_eq!(reply.files.len() + reply.dirs.len(), count);
        assert_eq!(reply.truncated, truncated);
        assert!(reply.gitignored_files.is_empty());
        if !truncated {
            assert_eq!(reply.files, ["visible.txt"]);
            assert_eq!(reply.dirs, ["visible-dir"]);
            assert_eq!(reply.empty_dirs, ["visible-dir"]);
        }
    }
    std::fs::remove_file(fixture.join("visible.txt")).unwrap();
    std::fs::remove_dir(fixture.join("visible-dir")).unwrap();
    let listing = list_dir(&fixture, true, false, 1, "", true).await.unwrap();
    assert!(listing.files.is_empty());
    assert!(!listing.truncated);
}
