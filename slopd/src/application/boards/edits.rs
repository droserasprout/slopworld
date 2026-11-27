//! Validate destination identities under the SQLite write lock. Shifting a suffix
//! leaves gaps at the source but preserves its order on retries and cross-board moves.
use crate::shared::wire::BoardEdit;
use anyhow::{Context, Result, bail, ensure};
use rusqlite::{Transaction, params};

fn required(value: &str, what: &str) -> Result<()> {
    ensure!(!value.trim().is_empty(), "{what} is required");
    Ok(())
}

pub(super) fn apply(tx: &Transaction<'_>, e: &BoardEdit, author: &str) -> Result<()> {
    let archived: bool = tx
        .query_row("SELECT archived FROM boards WHERE id=?", [&e.board], |r| {
            r.get(0)
        })
        .context("board no longer exists")?;
    if e.action == "delete-archive" {
        ensure!(
            archived && e.confirm,
            "deleting an archived board requires explicit confirmation"
        );
        tx.execute("DELETE FROM boards WHERE id=?", [&e.board])?;
        return Ok(());
    }
    ensure!(!archived, "board is archived");
    match e.action.as_str() {
        "add-column" => {
            required(&e.text, "column name")?;
            let position = column_position(tx, e)?;
            tx.execute(
                "INSERT INTO columns(id,board,position,name) VALUES(?,?,?,?)",
                params![uuid::Uuid::new_v4().to_string(), e.board, position, e.text],
            )?;
        }
        "rename-column" | "move-column" | "remove-column" | "add-card" | "move-card" => {
            edit_column(tx, e)?;
        }
        _ => edit_card(tx, e, author)?,
    }
    Ok(())
}

fn edit_column(tx: &Transaction<'_>, e: &BoardEdit) -> Result<()> {
    let exists: bool = tx.query_row(
        "SELECT EXISTS(SELECT 1 FROM columns WHERE id=? AND board=?)",
        params![e.column, e.board],
        |r| r.get(0),
    )?;
    ensure!(exists, "destination column no longer belongs to this board");
    match e.action.as_str() {
        "rename-column" => {
            required(&e.text, "column name")?;
            tx.execute(
                "UPDATE columns SET name=? WHERE id=?",
                params![e.text, e.column],
            )?;
        }
        "remove-column" => {
            let removed = tx.execute(
                "DELETE FROM columns WHERE id=? AND NOT EXISTS(SELECT 1 FROM cards WHERE col=?1)",
                [&e.column],
            )?;
            ensure!(removed == 1, "move all cards out of the column first");
        }
        "move-column" => {
            if e.before != e.column {
                let position = column_position(tx, e)?;
                tx.execute(
                    "UPDATE columns SET position=? WHERE id=?",
                    params![position, e.column],
                )?;
            }
        }
        "add-card" => {
            required(&e.text, "card title")?;
            let position = card_position(tx, e)?;
            tx.execute(
                "INSERT INTO cards(id,col,position,title) VALUES(?,?,?,?)",
                params![uuid::Uuid::new_v4().to_string(), e.column, position, e.text],
            )?;
        }
        _ => {
            let position = card_position(tx, e)?;
            ensure!(
                tx.execute(
                    "UPDATE cards SET col=?,position=? WHERE id=?",
                    params![e.column, position, e.card]
                )? == 1,
                "card no longer exists"
            );
        }
    }
    Ok(())
}

fn edit_card(tx: &Transaction<'_>, e: &BoardEdit, author: &str) -> Result<()> {
    let exists: bool = tx.query_row(
        "SELECT EXISTS (SELECT 1 FROM cards JOIN columns ON columns.id=cards.col
         WHERE cards.id=? AND columns.board=?)",
        params![e.card, e.board],
        |r| r.get(0),
    )?;
    ensure!(exists, "card no longer belongs to this board");
    match e.action.as_str() {
        "title" => {
            required(&e.text, "card title")?;
            tx.execute(
                "UPDATE cards SET title=? WHERE id=?",
                params![e.text, e.card],
            )?;
        }
        "describe" => {
            tx.execute(
                "UPDATE cards SET description=? WHERE id=?",
                params![e.text, e.card],
            )?;
        }
        "assign" => {
            tx.execute(
                "UPDATE cards SET assignee=? WHERE id=?",
                params![e.text.trim(), e.card],
            )?;
        }
        "comment" => {
            required(&e.text, "comment")?;
            tx.execute(
                "INSERT INTO comments(card,author,text) VALUES(?,?,?)",
                params![e.card, author, e.text],
            )?;
        }
        "link" => {
            required(&e.kind, "resource kind")?;
            required(&e.text, "resource target")?;
            tx.execute(
                "INSERT OR IGNORE INTO links(card,kind,target) VALUES(?,?,?)",
                params![e.card, e.kind, e.text],
            )?;
        }
        "unlink" => {
            tx.execute(
                "DELETE FROM links WHERE card=? AND kind=? AND target=?",
                params![e.card, e.kind, e.text],
            )?;
        }
        "remove-card" => {
            tx.execute("DELETE FROM cards WHERE id=?", [&e.card])?;
        }
        _ => bail!("unknown board action"),
    }
    Ok(())
}

fn column_position(tx: &Transaction<'_>, e: &BoardEdit) -> Result<i64> {
    if e.before.is_empty() {
        return Ok(tx.query_row(
            "SELECT COALESCE(MAX(position)+1,0) FROM columns WHERE board=?",
            [&e.board],
            |r| r.get(0),
        )?);
    }
    let position = tx
        .query_row(
            "SELECT position FROM columns WHERE board=? AND id=?",
            params![e.board, e.before],
            |r| r.get(0),
        )
        .context("destination column moved or disappeared")?;
    tx.execute(
        "UPDATE columns SET position=position+1 WHERE board=? AND position>=?",
        params![e.board, position],
    )?;
    Ok(position)
}
fn card_position(tx: &Transaction<'_>, e: &BoardEdit) -> Result<i64> {
    if e.before.is_empty() {
        return Ok(tx.query_row(
            "SELECT COALESCE(MAX(position)+1,0) FROM cards WHERE col=?",
            [&e.column],
            |r| r.get(0),
        )?);
    }
    let position = tx
        .query_row(
            "SELECT position FROM cards WHERE col=? AND id=?",
            params![e.column, e.before],
            |r| r.get(0),
        )
        .context("destination card moved or disappeared")?;
    tx.execute(
        "UPDATE cards SET position=position+1 WHERE col=? AND position>=?",
        params![e.column, position],
    )?;
    Ok(position)
}
