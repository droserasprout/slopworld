//! Drive complete collaboration and archival through the persisted store.
use super::*;
use wire::BoardEdit;

#[test]
fn column_transition_is_recorded_once_across_retries() {
    let mut db = Connection::open_in_memory().unwrap();
    db.execute_batch(include_str!("schema.sql")).unwrap();
    let tx = db.transaction().unwrap();
    ensure_board(&tx, "root", "Root", "root").unwrap();
    let board = snapshot::read(&tx).unwrap().boards.remove(0);
    let mut edit = BoardEdit {
        board: board.id,
        column: board.columns[0].id.clone(),
        action: "add-card".into(),
        text: "Read the feed".into(),
        ..Default::default()
    };
    edits::apply(&tx, &edit, "host").unwrap();
    edit.card = snapshot::read(&tx).unwrap().boards[0].columns[0].cards[0]
        .id
        .clone();
    edit.action = "move-card".into();
    edits::apply(&tx, &edit, "host").unwrap();
    edit.column.clone_from(&board.columns[1].id);
    for _ in 0..2 {
        edits::apply(&tx, &edit, "host").unwrap();
    }
    let board = snapshot::read(&tx).unwrap().boards.remove(0);
    let activity = &board.columns[1].cards[0].activity;
    assert_eq!(activity.len(), 2);
    assert_eq!(
        activity[1].text,
        format!(
            "Moved: {} → {}",
            board.columns[0].name, board.columns[1].name
        )
    );
}

async fn enable_project_boards(store: &Store, projects: &[(String, String)]) -> wire::BoardCatalog {
    assert!(
        store
            .run(projects.to_vec(), None)
            .await
            .unwrap()
            .boards
            .is_empty()
    );
    for (id, name) in projects {
        store
            .run(
                projects.to_vec(),
                Some((
                    BoardEdit {
                        board: id.clone(),
                        text: name.clone(),
                        action: "enable-board".into(),
                        ..Default::default()
                    },
                    "host".into(),
                )),
            )
            .await
            .unwrap();
    }
    store.run(projects.to_vec(), None).await.unwrap()
}

#[tokio::test]
async fn move_from_archive_is_rejected_and_preserves_discussion() {
    let path = std::env::temp_dir().join(format!("boards-{}.sqlite3", uuid::Uuid::new_v4()));
    let store = Store::new(path.clone());
    let projects = vec![
        ("root".into(), "Root".into()),
        ("stable".into(), "Project".into()),
    ];
    let catalog = enable_project_boards(&store, &projects).await;
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
    let root = vec![("root".into(), "Root".into())];
    let catalog = store.run(root.clone(), None).await.unwrap();
    assert!(catalog.boards[1].archived);
    assert!(
        store
            .run(root.clone(), Some((edit.clone(), "agent".into())))
            .await
            .unwrap_err()
            .to_string()
            .contains("board is archived")
    );
    edit.board = "root".into();
    edit.column = done;
    edit.action = "move-card".into();
    assert!(
        store
            .run(root.clone(), Some((edit.clone(), "human".into())))
            .await
            .unwrap_err()
            .to_string()
            .contains("card no longer belongs to this board")
    );
    let card = &catalog.boards[1].columns[0].cards[0];
    assert_eq!(card.key, "Project-1");
    assert_eq!(card.id, edit.card);
    assert_eq!(card.comments[0].author, "agent");
    edit.board = "stable".into();
    edit.action = "delete-archive".into();
    assert!(
        store
            .run(root.clone(), Some((edit.clone(), "human".into())))
            .await
            .unwrap_err()
            .to_string()
            .contains("requires explicit confirmation")
    );
    edit.confirm = true;
    store
        .run(root.clone(), Some((edit, "human".into())))
        .await
        .unwrap();
    let reopened_store = Store::new(path.clone());
    let reservations = reopened_store.sync_projects(root.clone()).await.unwrap();
    assert!(reservations.contains(&"stable".into()));
    let reopened = reopened_store.run(root, None).await.unwrap();
    assert_eq!(reopened.boards.len(), 1);
    assert!(reopened.boards[0].columns[3].cards.is_empty());
    std::fs::remove_file(path).unwrap();
}

#[test]
fn exact_insertion_and_repeated_link_removal_preserve_neighbors() {
    let mut db = Connection::open_in_memory().unwrap();
    db.execute_batch(include_str!("schema.sql")).unwrap();
    let tx = db.transaction().unwrap();
    ensure_board(&tx, "root", "Root", "root").unwrap();
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

#[tokio::test]
async fn concurrent_creation_and_retirement_survive_restart_and_prefix_change() {
    let path = std::env::temp_dir().join(format!("boards-{}.sqlite3", uuid::Uuid::new_v4()));
    let store = Store::new(path.clone());
    let projects = vec![
        ("root".into(), "Root".into()),
        ("sw".into(), "Before rename".into()),
    ];
    let catalog = enable_project_boards(&store, &projects).await;
    assert_eq!(catalog.boards[0].prefix, "Root");
    let mut edit = BoardEdit {
        action: "add-card".into(),
        board: "sw".into(),
        column: catalog.boards[1].columns[0].id.clone(),
        text: "plan".into(),
        ..Default::default()
    };
    let mut writers = tokio::task::JoinSet::new();
    for _ in 0..8 {
        let (store, projects, edit) = (store.clone(), projects.clone(), edit.clone());
        writers.spawn(async move {
            store
                .run(projects, Some((edit, "host".into())))
                .await
                .unwrap()
        });
    }
    while let Some(result) = writers.join_next().await {
        result.unwrap();
    }
    let store = Store::new(path.clone());
    let projects = vec![
        ("root".into(), "Root".into()),
        ("sw".into(), "Renamed".into()),
    ];
    let catalog = store.run(projects.clone(), None).await.unwrap();
    let board = &catalog.boards[1];
    assert_eq!(board.name, "Before rename");
    for (index, card) in board.columns[0].cards.iter().enumerate() {
        assert_eq!(card.key, format!("Before-rename-{}", index + 1));
    }
    let original = board.columns[0].cards[0].clone();
    edit.card = "Before-rename-8".into();
    edit.action = "remove-card".into();
    store
        .run(projects.clone(), Some((edit.clone(), "host".into())))
        .await
        .unwrap();
    let store = Store::new(path.clone());
    edit.action = "set-prefix".into();
    edit.text = "PLAN".into();
    store
        .run(projects.clone(), Some((edit.clone(), "host".into())))
        .await
        .unwrap();
    edit.action = "add-card".into();
    edit.card.clear();
    let catalog = store
        .run(projects.clone(), Some((edit.clone(), "host".into())))
        .await
        .unwrap();
    assert_eq!(
        catalog.boards[1].columns[0].cards.last().unwrap().key,
        "PLAN-9"
    );
    edit.action = "move-card".into();
    edit.card = original.key.clone();
    edit.column = board.columns[1].id.clone();
    let catalog = store
        .run(projects.clone(), Some((edit.clone(), "host".into())))
        .await
        .unwrap();
    let moved = &catalog.boards[1].columns[1].cards[0];
    assert_eq!(moved.key, "PLAN-1");
    assert_eq!(moved.id, original.id);
    assert_eq!(moved.previous_keys, [original.key]);
    for card in &catalog.boards[1].columns[0].cards {
        assert!(card.key.starts_with("PLAN-"));
    }
    edit.board = "root".into();
    edit.action = "set-prefix".into();
    edit.text = "Before-rename".into();
    assert!(
        store
            .run(projects, Some((edit, "host".into())))
            .await
            .unwrap_err()
            .to_string()
            .contains("prefix already reserved")
    );
    std::fs::remove_file(path).unwrap();
}

#[test]
fn default_prefix_avoids_root_and_other_reserved_project_names() {
    let mut db = Connection::open_in_memory().unwrap();
    db.execute_batch(include_str!("schema.sql")).unwrap();
    let tx = db.transaction().unwrap();
    ensure_board(&tx, "root", "Root", "root").unwrap();
    ensure_board(&tx, "one", "Root", "").unwrap();
    ensure_board(&tx, "two", "Root-2", "").unwrap();
    let catalog = snapshot::read(&tx).unwrap();
    assert_eq!(
        catalog
            .boards
            .iter()
            .map(|b| b.prefix.as_str())
            .collect::<Vec<_>>(),
        ["root", "Root-2", "Root-2-2"]
    );
}

#[test]
fn rekeying_recycles_the_previous_key_and_preserves_relations() {
    let mut db = Connection::open_in_memory().unwrap();
    db.execute_batch(include_str!("schema.sql")).unwrap();
    let tx = db.transaction().unwrap();
    ensure_board(&tx, "root", "Root", "root").unwrap();
    ensure_board(&tx, "p", "P", "").unwrap();
    let mut edit = BoardEdit {
        board: "root".into(),
        ..Default::default()
    };
    let boards = snapshot::read(&tx).unwrap().boards;
    for board in &boards {
        edit.board.clone_from(&board.id);
        edit.action = "add-card".into();
        edit.column.clone_from(&board.columns[0].id);
        edit.text = "plan".into();
        edits::apply(&tx, &edit, "human").unwrap();
    }
    edit.board = "root".into();
    edit.card = "root-1".into();
    edit.action = "link".into();
    edit.kind = "task".into();
    edit.text = "task-id".into();
    edits::apply(&tx, &edit, "human").unwrap();
    edit.action = "comment".into();
    edit.text = "still reachable".into();
    edits::apply(&tx, &edit, "human").unwrap();
    let mut before = snapshot::read(&tx).unwrap().boards[0].columns[0].cards[0].clone();
    for prefix in ["older", "my-2nd-board", "root", "root"] {
        edit.action = "set-prefix".into();
        edit.text = prefix.into();
        edits::apply(&tx, &edit, "human").unwrap();
    }
    edit.card = "older-1".into();
    edit.action = "comment".into();
    edit.text = "stale write".into();
    edits::apply(&tx, &edit, "human").unwrap_err();
    let boards = snapshot::read(&tx).unwrap().boards;
    let card = &boards[0].columns[0].cards[0];
    assert_eq!(card.key, "root-1");
    before.previous_keys = vec!["my-2nd-board-1".into()];
    assert_eq!(card, &before);
    assert_eq!(card.links[0].target, "task-id");
    assert_eq!(card.comments[0].text, "still reachable");
    assert_eq!(boards[1].columns[0].cards[0].key, "P-1");
    edit.card = "my-2nd-board-1".into();
    edits::apply(&tx, &edit, "human").unwrap();
}
