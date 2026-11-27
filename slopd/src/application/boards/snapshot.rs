//! Read the ordered board tree inside the same transaction as its mutation.
use crate::shared::wire::{
    BoardCard, BoardCatalog, BoardColumn, BoardComment, BoardLink, ProjectBoard,
};
use rusqlite::{Params, Result, Row, Transaction};

fn rows<T>(
    tx: &Transaction<'_>,
    sql: &str,
    args: impl Params,
    read: impl FnMut(&Row<'_>) -> Result<T>,
) -> Result<Vec<T>> {
    tx.prepare_cached(sql)?.query_map(args, read)?.collect()
}

pub(super) fn read(tx: &Transaction<'_>) -> Result<BoardCatalog> {
    read_filtered(tx, None)
}

pub(super) fn read_filtered(tx: &Transaction<'_>, task: Option<&str>) -> Result<BoardCatalog> {
    let mut boards = rows(
        tx,
        "SELECT id,name,archived,prefix FROM boards WHERE archived!=2 ORDER BY archived,id!='root',name,id",
        [],
        |r| {
            Ok(ProjectBoard {
                id: r.get(0)?,
                name: r.get(1)?,
                archived: r.get(2)?,
                prefix: r.get(3)?,
                ..Default::default()
            })
        },
    )?;
    for board in &mut boards {
        board.columns = rows(
            tx,
            "SELECT id,name FROM columns WHERE board=? ORDER BY position,id",
            [&board.id],
            |r| {
                Ok(BoardColumn {
                    id: r.get(0)?,
                    name: r.get(1)?,
                    ..Default::default()
                })
            },
        )?;
        for column in &mut board.columns {
            column.cards = rows(
                tx,
                "SELECT id,title,description,assignee,public_key FROM cards WHERE col=?1
                 AND (?2 IS NULL OR id IN (SELECT card FROM links WHERE kind='task' AND target=?2))
                 ORDER BY position,id",
                rusqlite::params![column.id, task],
                |r| {
                    Ok(BoardCard {
                        id: r.get(0)?,
                        title: r.get(1)?,
                        description: r.get(2)?,
                        assignee: r.get(3)?,
                        key: r.get(4)?,
                        ..Default::default()
                    })
                },
            )?;
            for card in &mut column.cards {
                card.previous_keys = rows(
                    tx,
                    "SELECT public_key FROM card_key_aliases WHERE card=? AND public_key!=? ORDER BY public_key",
                    rusqlite::params![card.id, card.key],
                    |r| r.get(0),
                )?;
                card.comments = rows(
                    tx,
                    "SELECT author,text,timestamp_ms,sequence FROM comments WHERE card=? ORDER BY sequence",
                    [&card.id],
                    discussion_entry,
                )?;
                card.activity = rows(
                    tx,
                    "SELECT author,text,timestamp_ms,sequence FROM card_activity WHERE card=? ORDER BY sequence",
                    [&card.id],
                    discussion_entry,
                )?;
                card.links = rows(
                    tx,
                    "SELECT kind,target FROM links WHERE card=? ORDER BY kind,target",
                    [&card.id],
                    |r| {
                        Ok(BoardLink {
                            kind: r.get(0)?,
                            target: r.get(1)?,
                        })
                    },
                )?;
            }
        }
    }
    if task.is_some() {
        for board in &mut boards {
            board.columns.retain(|column| !column.cards.is_empty());
        }
        boards.retain(|board| !board.columns.is_empty());
    }
    Ok(BoardCatalog {
        boards,
        ..Default::default()
    })
}

fn discussion_entry(row: &Row<'_>) -> Result<BoardComment> {
    Ok(BoardComment {
        author: row.get(0)?,
        text: row.get(1)?,
        timestamp_ms: row.get(2)?,
        id: row.get(3)?,
    })
}
