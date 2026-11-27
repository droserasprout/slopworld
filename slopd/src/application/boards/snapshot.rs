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
    let mut boards = rows(
        tx,
        "SELECT id,name,archived FROM boards ORDER BY archived,id!='root',name,id",
        [],
        |r| {
            Ok(ProjectBoard {
                id: r.get(0)?,
                name: r.get(1)?,
                archived: r.get(2)?,
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
                "SELECT id,title,description,assignee FROM cards WHERE col=? ORDER BY position,id",
                [&column.id],
                |r| {
                    Ok(BoardCard {
                        id: r.get(0)?,
                        title: r.get(1)?,
                        description: r.get(2)?,
                        assignee: r.get(3)?,
                        ..Default::default()
                    })
                },
            )?;
            for card in &mut column.cards {
                card.comments = rows(
                    tx,
                    "SELECT author,text FROM comments WHERE card=? ORDER BY sequence",
                    [&card.id],
                    |r| {
                        Ok(BoardComment {
                            author: r.get(0)?,
                            text: r.get(1)?,
                        })
                    },
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
    Ok(BoardCatalog { boards })
}
