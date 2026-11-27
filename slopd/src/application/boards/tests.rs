//! Drive complete collaboration and archival through the persisted store.
use super::*;
use wire::BoardEdit;

#[tokio::test]
async fn move_from_archive_preserves_card_and_rejects_stale_destinations() {
    let path = std::env::temp_dir().join(format!("boards-{}.sqlite3", uuid::Uuid::new_v4()));
    let store = Store::new(path.clone());
    let projects = vec![("stable".into(), "Project".into())];
    let catalog = store.run(projects.clone(), None).await.unwrap();
    let backlog = catalog.boards[1].columns[0].id.clone();
    let done = catalog.boards[0].columns[3].id.clone();
    let mut edit = BoardEdit {
        action: "add-card".into(),
        board: "stable".into(),
        column: backlog,
        text: "Review".into(),
        ..Default::default()
    };
    let catalog = store
        .run(projects.clone(), Some((edit.clone(), "human".into())))
        .await
        .unwrap();
    edit.card = catalog.boards[1].columns[0].cards[0].id.clone();
    for (action, text) in [
        ("assign", "agent"),
        ("link", "checkout"),
        ("comment", "ready"),
    ] {
        edit.action = action.into();
        edit.text = text.into();
        edit.kind = "worktree".into();
        store
            .run(projects.clone(), Some((edit.clone(), "agent".into())))
            .await
            .unwrap();
    }
    let catalog = store.run(vec![], None).await.unwrap();
    assert!(catalog.boards[1].archived);
    assert!(
        store
            .run(vec![], Some((edit.clone(), "agent".into())))
            .await
            .unwrap_err()
            .to_string()
            .contains("board is archived")
    );
    edit.board = "root".into();
    edit.column = done;
    edit.action = "move-card".into();
    edit.before = "gone".into();
    assert!(
        store
            .run(vec![], Some((edit.clone(), "human".into())))
            .await
            .unwrap_err()
            .to_string()
            .contains("destination card moved or disappeared")
    );
    edit.before.clear();
    let catalog = store
        .run(vec![], Some((edit.clone(), "human".into())))
        .await
        .unwrap();
    let card = &catalog.boards[0].columns[3].cards[0];
    assert_eq!(card.id, edit.card);
    assert_eq!(card.assignee, "agent");
    assert_eq!(card.comments[0].author, "agent");
    assert_eq!(card.links[0].target, "checkout");
    assert!(catalog.boards[1].columns[0].cards.is_empty());
    edit.board = "stable".into();
    edit.action = "delete-archive".into();
    assert!(
        store
            .run(vec![], Some((edit.clone(), "human".into())))
            .await
            .unwrap_err()
            .to_string()
            .contains("requires explicit confirmation")
    );
    edit.confirm = true;
    store
        .run(vec![], Some((edit, "human".into())))
        .await
        .unwrap();
    let reopened = Store::new(path.clone()).run(vec![], None).await.unwrap();
    assert_eq!(reopened.boards.len(), 1);
    assert_eq!(reopened.boards[0].columns[3].cards[0], *card);
    std::fs::remove_file(path).unwrap();
}

#[test]
fn exact_insertion_and_repeated_link_removal_preserve_neighbors() {
    let mut db = Connection::open_in_memory().unwrap();
    db.execute_batch(include_str!("schema.sql")).unwrap();
    let tx = db.transaction().unwrap();
    reconcile(&tx, &[]).unwrap();
    let mut edit = wire::BoardEdit {
        board: "root".into(),
        action: "add-card".into(),
        column: snapshot::read(&tx).unwrap().boards[0].columns[0].id.clone(),
        ..Default::default()
    };
    for title in ["A", "B", "C"] {
        edit.text = title.into();
        edits::apply(&tx, &edit, "agent").unwrap();
    }
    let board = snapshot::read(&tx).unwrap().boards.remove(0);
    let [a, b, c] = board.columns[0].cards.as_slice() else {
        panic!("expected three cards")
    };
    edit.card = c.id.clone();
    edit.before = b.id.clone();
    edit.action = "move-card".into();
    edits::apply(&tx, &edit, "agent").unwrap();
    edits::apply(&tx, &edit, "agent").unwrap();
    edit.action = "title".into();
    edit.text = "Renamed".into();
    edits::apply(&tx, &edit, "agent").unwrap();
    edit.action = "describe".into();
    edit.text = "Body".into();
    edits::apply(&tx, &edit, "agent").unwrap();
    edit.kind = "branch".into();
    edit.text = "main".into();
    for action in ["link", "link", "unlink", "unlink"] {
        edit.action = action.into();
        edits::apply(&tx, &edit, "agent").unwrap();
    }
    edit.action = "rename-column".into();
    edit.text = "Review".into();
    edits::apply(&tx, &edit, "agent").unwrap();
    let board = snapshot::read(&tx).unwrap().boards.remove(0);
    let column = &board.columns[0];
    assert_eq!(column.name, "Review");
    assert_eq!(
        column.cards.iter().map(|card| &card.id).collect::<Vec<_>>(),
        [&a.id, &c.id, &b.id]
    );
    assert_eq!(column.cards[1].description, "Body");
    assert!(column.cards[1].links.is_empty());
    edit.action = "remove-column".into();
    edits::apply(&tx, &edit, "agent").unwrap_err();
    edit.action = "remove-card".into();
    for card in [a, b, c] {
        edit.card = card.id.clone();
        edits::apply(&tx, &edit, "agent").unwrap();
    }
    edit.action = "remove-column".into();
    edits::apply(&tx, &edit, "agent").unwrap();
    assert_eq!(snapshot::read(&tx).unwrap().boards[0].columns.len(), 3);
}
