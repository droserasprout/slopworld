use super::*;
use crate::config::LibraryItemKind;

fn item(name: &str) -> LibraryItemCfg {
    LibraryItemCfg {
        name: name.into(),
        kind: LibraryItemKind::Breadcrumb,
        text: "Review the change".into(),
        ..Default::default()
    }
}

#[tokio::test]
async fn failed_discovery_retains_snapshot_and_next_success_replaces_it() {
    let catalog = LibraryCatalog::default();
    catalog.seed(vec![item("previous")]);
    let failure = tokio::spawn(async { panic!("discovery failed") })
        .await
        .unwrap_err();
    assert_eq!(catalog.accept(Err(failure))[0].name, "previous");
    assert_eq!(
        catalog.accept(Ok(vec![item("recovered")]))[0].name,
        "recovered"
    );
    assert_eq!(catalog.snapshot.lock().unwrap()[0].name, "recovered");
}

#[test]
fn duplicate_and_conflicting_renames_leave_catalog_unchanged() {
    let mut cfg = Config::default();
    LibraryMutation::Add(item("first")).apply(&mut cfg).unwrap();
    LibraryMutation::Add(item("second"))
        .apply(&mut cfg)
        .unwrap();
    assert!(LibraryMutation::Add(item("first")).apply(&mut cfg).is_err());
    assert!(
        LibraryMutation::Replace {
            name: "first".into(),
            item: item("second")
        }
        .apply(&mut cfg)
        .is_err()
    );
    assert_eq!(
        cfg.library
            .iter()
            .map(|i| i.name.as_str())
            .collect::<Vec<_>>(),
        ["first", "second"]
    );
    LibraryMutation::Replace {
        name: "first".into(),
        item: item("renamed"),
    }
    .apply(&mut cfg)
    .unwrap();
    LibraryMutation::Remove("renamed".into())
        .apply(&mut cfg)
        .unwrap();
    assert!(
        LibraryMutation::Remove("renamed".into())
            .apply(&mut cfg)
            .is_err()
    );
    assert_eq!(cfg.library[0].name, "second");
}

#[test]
fn breadcrumb_preparation_uses_resolved_context_and_preserves_literal_replacements() {
    let cfg = Config {
        library: vec![LibraryItemCfg {
            name: "context".into(), kind: LibraryItemKind::Breadcrumb,
            text: "{{ agent }}|{{ project }}|{{ directory }}|{{ command }}|{{ random_tip }}|{{ random_tip }}".into(),
            ..Default::default()
        }],
        ..Default::default()
    };
    let session = crate::config::SessionCfg {
        name: "agent".into(),
        cmd: Some("codex --yolo".into()),
        ..Default::default()
    };
    let project = crate::config::ProjectCfg {
        name: "repo".into(),
        dir: "/resolved/worktree".into(),
        ..Default::default()
    };
    assert_eq!(
        prepare_breadcrumb(
            &cfg,
            &session,
            &project,
            "context",
            &["{{ agent }}".into(), "second".into()]
        )
        .unwrap(),
        "agent|repo|/resolved/worktree|codex --yolo|{{ agent }}|second"
    );
    prepare_breadcrumb(&cfg, &session, &project, "missing", &[]).unwrap_err();
    let mut wrong_kind = cfg;
    wrong_kind.library[0].kind = LibraryItemKind::Prompt;
    prepare_breadcrumb(&wrong_kind, &session, &project, "context", &[]).unwrap_err();
}
