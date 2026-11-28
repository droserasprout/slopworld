//! Validate destination identities under the SQLite write lock. Shifting a suffix
//! leaves gaps at the source but preserves its order on retries and moves between columns.
use crate::shared::wire::BoardEdit;
use anyhow::{Context, Result, bail, ensure};
use rusqlite::{Transaction, params};

fn required(value: &str, what: &str) -> Result<()> {
    ensure!(!value.trim().is_empty(), "{what} is required");
    Ok(())
}

pub(super) fn apply(tx: &Transaction<'_>, e: &BoardEdit, author: &str) -> Result<()> {
    let archived: bool = tx
        .query_row(
            "SELECT archived FROM boards WHERE id=? AND archived!=2",
            [&e.board],
            |r| r.get(0),
        )
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
    let mut resolved = e.clone();
    if !matches!(
        e.action.as_str(),
        "add-column"
            | "remove-column"
            | "rename-column"
            | "move-column"
            | "add-card"
            | "set-prefix"
    ) {
        resolved.card = resolve_card(tx, &e.board, &e.card)?;
    }
    if matches!(e.action.as_str(), "move-card" | "add-card") && !e.before.is_empty() {
        resolved.before = resolve_card(tx, &e.board, &e.before)?;
    }
    let e = &resolved;
    match e.action.as_str() {
        "set-prefix" => {
            required(&e.text, "prefix")?;
            ensure!(
                e.text
                    .bytes()
                    .all(|b| b.is_ascii_alphanumeric() || b == b'-' || b == b'_'),
                "invalid prefix"
            );
            super::reserve_prefix(tx, &e.board, &e.text)?;
            // The board-wide counter gives each card a unique numeric suffix across all its prefixes.
            tx.execute(
                "UPDATE cards SET public_key=?1 || '-' || substr(public_key,length(rtrim(public_key,'0123456789'))+1)
                 WHERE col IN (SELECT id FROM columns WHERE board=?2)", params![e.text,e.board])?;
            tx.execute(
                "UPDATE boards SET prefix=? WHERE id=?",
                params![e.text, e.board],
            )?;
        }
        "add-column" => {
            required(&e.text, "column name")?;
            let position = column_position(tx, e)?;
            tx.execute(
                "INSERT INTO columns(id,board,position,name) VALUES(?,?,?,?)",
                params![uuid::Uuid::new_v4().to_string(), e.board, position, e.text],
            )?;
        }
        "rename-column" | "move-column" | "remove-column" | "add-card" | "move-card" => {
            edit_column(tx, e, author)?;
        }
        _ => edit_card(tx, e, author)?,
    }
    Ok(())
}

fn resolve_card(tx: &Transaction<'_>, board: &str, reference: &str) -> Result<String> {
    tx.query_row("SELECT cards.id FROM cards JOIN columns ON cards.col=columns.id WHERE columns.board=?1 AND (cards.id=?2 OR cards.public_key=?2 OR cards.id=(SELECT card FROM card_key_aliases WHERE public_key=?2))", params![board,reference], |r| r.get(0)).context("card no longer belongs to this board")
}

fn edit_column(tx: &Transaction<'_>, e: &BoardEdit, author: &str) -> Result<()> {
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
            let id = uuid::Uuid::new_v4().to_string();
            let number: i64 = tx.query_row(
                "UPDATE board_counters SET issued=issued+1 WHERE owner=? RETURNING issued",
                [&e.board],
                |r| r.get(0),
            )?;
            tx.execute(
                "INSERT INTO cards(id,col,position,title,public_key) SELECT ?,?,?,?,prefix || '-' || ? FROM boards WHERE id=?",
                params![id, e.column, position, e.text, number, e.board],
            )?;
            column_activity(tx, &id, author, "Added to", &e.column)?;
        }
        _ => {
            let position = card_position(tx, e)?;
            // Capture both names before updating the card. A reorder or retried move
            // within the destination column has no status transition.
            tx.execute(
                "INSERT INTO card_activity(card,author,text,timestamp_ms)
                 SELECT cards.id,?2,'Moved: ' || src.name || ' → ' || dst.name,CAST(unixepoch('subsec')*1000 AS INTEGER)
                 FROM cards JOIN columns src ON src.id=cards.col JOIN columns dst ON dst.id=?3
                 WHERE cards.id=?1 AND src.id!=dst.id",
                params![e.card, author, e.column],
            )?;
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

fn column_activity(
    tx: &Transaction<'_>,
    card: &str,
    author: &str,
    verb: &str,
    column: &str,
) -> Result<()> {
    tx.execute(
        "INSERT INTO card_activity(card,author,text,timestamp_ms) SELECT ?1,?2,?3 || ' ' || name,CAST(unixepoch('subsec')*1000 AS INTEGER) FROM columns WHERE id=?4",
        params![card, author, verb, column],
    )?;
    Ok(())
}

fn edit_card(tx: &Transaction<'_>, e: &BoardEdit, author: &str) -> Result<()> {
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
        "edit-comment" => {
            required(&e.text, "comment")?;
            ensure!(
                tx.execute(
                    "UPDATE comments SET text=? WHERE card=? AND sequence=?",
                    params![e.text, e.card, e.entry_id],
                )? == 1,
                "comment no longer exists"
            );
        }
        "remove-comment" | "remove-activity" => {
            let table = if e.action == "remove-comment" {
                "comments"
            } else {
                "card_activity"
            };
            ensure!(
                tx.execute(
                    &format!("DELETE FROM {table} WHERE card=? AND sequence=?"),
                    params![e.card, e.text.parse::<i64>()?]
                )? == 1,
                "entry no longer exists"
            );
        }
        "comment" => {
            required(&e.text, "comment")?;
            tx.execute(
                "INSERT INTO comments(card,author,text,timestamp_ms) VALUES(?,?,?,CAST(unixepoch('subsec')*1000 AS INTEGER))",
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
    let activity = match e.action.as_str() {
        "title" => "Changed the title",
        "describe" => "Edited the description",
        "assign" => "Updated the assignment",
        "link" => "Attached a link",
        "unlink" => "Removed a link",
        _ => return Ok(()),
    };
    if tx.changes() > 0 {
        tx.execute(
            "INSERT INTO card_activity(card,author,text,timestamp_ms) VALUES(?,?,?,CAST(unixepoch('subsec')*1000 AS INTEGER))",
            params![e.card, author, activity],
        )?;
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
