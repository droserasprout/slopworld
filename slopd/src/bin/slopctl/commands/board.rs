//! Board commands use stable IDs from list/show. The daemon validates destination
//! membership under its transaction and returns the committed catalog.
use super::super::{
    format::print_json,
    http::{Endpoint, request},
};
use crate::shared::protocol::routes;
use serde_json::{Value, json};

#[derive(Debug, PartialEq, Eq, clap::Subcommand)]
pub(crate) enum BoardArgs {
    /// List active and archived boards with their stable IDs
    List,
    /// Show ordered columns, cards, assignments, comments and resource links
    Show {
        #[arg(default_value = "root")]
        board: String,
    },
    /// Append a column, or insert it before another column
    AddColumn {
        board: String,
        name: String,
        #[arg(long)]
        before: Option<String>,
    },
    RenameColumn {
        board: String,
        column: String,
        name: String,
    },
    /// Reorder a column; omit --before to append
    MoveColumn {
        board: String,
        column: String,
        #[arg(long)]
        before: Option<String>,
    },
    /// Remove an empty column
    RemoveColumn {
        board: String,
        column: String,
    },
    AddCard {
        board: String,
        column: String,
        title: String,
        #[arg(long)]
        before: Option<String>,
    },
    /// Move an existing card to a destination board and column
    MoveCard {
        board: String,
        column: String,
        card: String,
        #[arg(long)]
        before: Option<String>,
    },
    RemoveCard {
        board: String,
        card: String,
    },
    Title {
        board: String,
        card: String,
        text: String,
    },
    Describe {
        board: String,
        card: String,
        text: String,
    },
    /// Set the sole assignee; omit name to assign yourself
    Assign {
        board: String,
        card: String,
        name: Option<String>,
    },
    Unassign {
        board: String,
        card: String,
    },
    Comment {
        board: String,
        card: String,
        text: String,
    },
    Link {
        board: String,
        card: String,
        kind: String,
        target: String,
    },
    Unlink {
        board: String,
        card: String,
        kind: String,
        target: String,
    },
    /// Permanently delete an archived board and its remaining cards
    DeleteArchive {
        board: String,
        #[arg(long, required = true)]
        confirm: bool,
    },
}

pub(super) fn run(endpoint: &Endpoint, session: &str, args: BoardArgs) -> Result<(), String> {
    use BoardArgs::*;
    let body = match args {
        List | Show { .. } => {
            let catalog = request(endpoint, session, "GET", routes::BOARDS, None)?;
            return show(&catalog, args);
        }
        AddColumn {
            board,
            name,
            before,
        } => {
            json!({"action":"add-column","board":board,"text":name,"before":before.unwrap_or_default()})
        }
        RenameColumn {
            board,
            column,
            name,
        } => json!({"action":"rename-column","board":board,"column":column,"text":name}),
        MoveColumn {
            board,
            column,
            before,
        } => {
            json!({"action":"move-column","board":board,"column":column,"before":before.unwrap_or_default()})
        }
        RemoveColumn { board, column } => {
            json!({"action":"remove-column","board":board,"column":column})
        }
        AddCard {
            board,
            column,
            title,
            before,
        } => {
            json!({"action":"add-card","board":board,"column":column,"text":title,"before":before.unwrap_or_default()})
        }
        MoveCard {
            board,
            column,
            card,
            before,
        } => {
            json!({"action":"move-card","board":board,"column":column,"card":card,"before":before.unwrap_or_default()})
        }
        RemoveCard { board, card } => json!({"action":"remove-card","board":board,"card":card}),
        Title { board, card, text } => {
            json!({"action":"title","board":board,"card":card,"text":text})
        }
        Describe { board, card, text } => {
            json!({"action":"describe","board":board,"card":card,"text":text})
        }
        Assign { board, card, name } => {
            json!({"action":"assign","board":board,"card":card,"text":name.as_deref().unwrap_or(session)})
        }
        Unassign { board, card } => json!({"action":"assign","board":board,"card":card,"text":""}),
        Comment { board, card, text } => {
            json!({"action":"comment","board":board,"card":card,"text":text})
        }
        Link {
            board,
            card,
            kind,
            target,
        } => json!({"action":"link","board":board,"card":card,"kind":kind,"text":target}),
        Unlink {
            board,
            card,
            kind,
            target,
        } => json!({"action":"unlink","board":board,"card":card,"kind":kind,"text":target}),
        DeleteArchive { board, confirm } => {
            json!({"action":"delete-archive","board":board,"confirm":confirm})
        }
    };
    print_json(&request(
        endpoint,
        session,
        "POST",
        routes::BOARDS,
        Some(body),
    )?);
    Ok(())
}

fn show(catalog: &Value, args: BoardArgs) -> Result<(), String> {
    let boards = catalog
        .get("boards")
        .and_then(Value::as_array)
        .ok_or("missing boards")?;
    if let BoardArgs::Show { board } = args {
        print_json(
            boards
                .iter()
                .find(|b| b.get("id").and_then(Value::as_str) == Some(board.as_str()))
                .ok_or("board not found")?,
        );
    } else {
        print_json(&Value::Array(
            boards
                .iter()
                .map(
                    |b| json!({"id":b.get("id"),"name":b.get("name"),"archived":b.get("archived")}),
                )
                .collect(),
        ));
    }
    Ok(())
}
