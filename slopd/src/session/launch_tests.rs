use super::*;
use crate::config::SessionCfg;
use serde_json::json;

#[test]
fn launch_requests_keep_flat_wire_fields_and_default_missing_context() {
    let empty: RunWhere = serde_json::from_value(json!({})).unwrap();
    assert_eq!(empty.context, LaunchContext::default());
    let request: RunWhere = serde_json::from_value(json!({
        "worktree": "checkout", "project": "repo", "temp": true,
        "cols": 90, "rows": 30, "random_tips": ["tip"],
        "intent": "view", "reader_path": "/tmp/file ", "reader_key": "key",
        "reader_scope": "scope", "reader_pinned": true, "reader_line": 42,
        "original_label": "untrusted"
    }))
    .unwrap();
    assert_eq!(request.context.intent, "view");
    assert_eq!(
        request.context.reader,
        ReaderContext {
            path: "/tmp/file ".into(),
            key: "key".into(),
            scope: "scope".into(),
            pinned: true,
            line: 42,
        }
    );
    assert!(request.context.original_label.is_empty());
    assert_eq!(request.worktree, "checkout");
    assert_eq!(request.project.as_deref(), Some("repo"));
    assert!(request.temp);
    assert_eq!((request.cols, request.rows), (Some(90), Some(30)));
    assert_eq!(request.random_tips, ["tip"]);
}

#[test]
fn reader_metadata_round_trip_preserves_context_and_uses_current_session_identity() {
    let context = LaunchContext {
        intent: "diff".into(),
        original_label: "original.rs".into(),
        reader: ReaderContext {
            path: "/tmp/file".into(),
            key: "key".into(),
            scope: "scope".into(),
            pinned: true,
            line: 12,
        },
    };
    let session = SessionCfg {
        label: Some("renamed".into()),
        project: "repo".into(),
        worktree: "checkout".into(),
        ..Default::default()
    };
    let metadata = context.metadata(&session).unwrap();
    assert_eq!(metadata.label, "renamed");
    assert_eq!(metadata.original_label, "original.rs");
    assert_eq!(metadata.project, "repo");
    assert_eq!(metadata.worktree, "checkout");
    assert_eq!(LaunchContext::from(&metadata), context);
    assert!(LaunchContext::default().metadata(&session).is_none());
    let stored = serde_json::to_value(&session).unwrap();
    for key in [
        "intent",
        "reader_label",
        "reader_path",
        "reader_key",
        "reader_scope",
        "reader_line",
        "reader_pinned",
        "launch",
        "context",
    ] {
        assert!(
            stored.get(key).is_none(),
            "runtime field {key} leaked into the definition"
        );
    }
}

#[test]
fn library_launches_resolve_overrides_without_carrying_catalog_fields() {
    use crate::config::{LibraryItemCfg, LibraryItemKind, LibraryItemLink};
    let item = LibraryItemCfg {
        name: "review".into(),
        kind: LibraryItemKind::Prompt,
        project: "saved".into(),
        link: LibraryItemLink::Temp,
        text: "review changes".into(),
        agent_template: " reviewer ".into(),
        builtin: true,
        ..Default::default()
    };
    let temporary = LaunchRequest::library(item.clone(), RunWhere::default()).unwrap();
    assert_eq!(temporary.destination, LaunchDestination::Temporary);
    assert_eq!(
        temporary.execution,
        ExecutionSource::Template("reviewer".into())
    );
    let selected = LaunchRequest::library(
        item.clone(),
        RunWhere {
            project: Some(" selected ".into()),
            worktree: "checkout".into(),
            ..Default::default()
        },
    )
    .unwrap();
    assert_eq!(
        selected.destination,
        LaunchDestination::Project("selected".into())
    );
    assert_eq!(selected.options.worktree, "checkout");
    assert_eq!(selected.content.text, item.text);
    let forced = LaunchRequest::library(
        item.clone(),
        RunWhere {
            project: Some("selected".into()),
            temp: true,
            ..Default::default()
        },
    )
    .unwrap();
    assert_eq!(forced.destination, LaunchDestination::Temporary);
    let mut ask = item;
    ask.link = LibraryItemLink::Ask;
    assert!(
        LaunchRequest::library(ask, RunWhere::default())
            .unwrap_err()
            .to_string()
            .contains("Choose a project")
    );
}
