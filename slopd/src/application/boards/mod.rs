//! SQLite owns board membership, ordering and card metadata. Accepted project IDs
//! supply board liveness. No board operation controls a session or a worktree.
mod edits;
mod snapshot;
use crate::shared::wire;
use anyhow::{Context, Result};
use rusqlite::{Connection, OptionalExtension, Transaction, TransactionBehavior, params};
use std::{path::PathBuf, time::Duration};

#[derive(Clone)]
pub(crate) struct Store(PathBuf);

impl Store {
    pub(crate) fn new(path: PathBuf) -> Self {
        Self(path)
    }

    pub(crate) async fn run(
        &self,
        projects: Vec<(String, String)>,
        edit: Option<(wire::BoardEdit, String)>,
    ) -> Result<wire::BoardCatalog> {
        self.transaction(projects, move |tx| {
            if let Some((edit, author)) = edit {
                edits::apply(tx, &edit, &author)?;
            }
            Ok(snapshot::read(tx)?)
        })
        .await
    }

    pub(crate) async fn linked_cards(
        &self,
        projects: Vec<(String, String)>,
        task: String,
    ) -> Result<wire::BoardCatalog> {
        self.transaction(projects, move |tx| {
            Ok(snapshot::read_filtered(tx, Some(&task))?)
        })
        .await
    }

    /// Lifecycle callers only need the identities still reserved by this store.
    pub(crate) async fn sync_projects(
        &self,
        projects: Vec<(String, String)>,
    ) -> Result<Vec<String>> {
        self.transaction(projects, |tx| {
            Ok(tx
                .prepare("SELECT lower(prefix) FROM board_prefixes UNION SELECT owner FROM board_counters")?
                .query_map([], |r| r.get(0))?
                .collect::<rusqlite::Result<_>>()?)
        })
        .await
    }

    async fn transaction<T: Send + 'static>(
        &self,
        projects: Vec<(String, String)>,
        operation: impl FnOnce(&Transaction<'_>) -> Result<T> + Send + 'static,
    ) -> Result<T> {
        let path = self.0.clone();
        tokio::task::spawn_blocking(move || {
            use std::os::unix::fs::{DirBuilderExt, OpenOptionsExt};
            let parent = path
                .parent()
                .context("board database needs a data directory")?;
            std::fs::DirBuilder::new()
                .recursive(true)
                .mode(0o700)
                .create(parent)
                .with_context(|| format!("creating board directory {}", parent.display()))?;
            std::fs::OpenOptions::new()
                .create(true)
                .truncate(false)
                .write(true)
                .mode(0o600)
                .open(&path)
                .with_context(|| format!("creating board database {}", path.display()))?;
            let mut db = Connection::open(path).context("opening board database")?;
            db.busy_timeout(Duration::from_secs(5))?;
            db.pragma_update(None, "foreign_keys", true)?;
            let tx = db.transaction_with_behavior(TransactionBehavior::Immediate)?;
            tx.execute_batch(include_str!("schema.sql"))?;
            reconcile(&tx, &projects)?;
            let result = operation(&tx)?;
            tx.commit()?;
            Ok(result)
        })
        .await?
    }
}

fn reconcile(tx: &Transaction<'_>, projects: &[(String, String)]) -> Result<()> {
    let mut active = tx.prepare("SELECT id FROM boards WHERE archived=0 AND id!='root'")?;
    for id in active
        .query_map([], |r| r.get::<_, String>(0))?
        .collect::<rusqlite::Result<Vec<_>>>()?
    {
        if !projects.iter().any(|(project, _)| *project == id) {
            tx.execute("UPDATE boards SET archived=1 WHERE id=?", [id])?;
        }
    }
    ensure_board(tx, "root", "Root")?;
    for (id, name) in projects {
        ensure_board(tx, id, name)?;
    }
    Ok(())
}

fn reserve_prefix(tx: &Transaction<'_>, owner: &str, prefix: &str) -> Result<()> {
    tx.execute(
        "INSERT OR IGNORE INTO board_prefixes(prefix,owner) VALUES(?,?)",
        params![prefix, owner],
    )?;
    let reserved: String = tx.query_row(
        "SELECT owner FROM board_prefixes WHERE prefix=?",
        [prefix],
        |r| r.get(0),
    )?;
    anyhow::ensure!(reserved == owner, "prefix already reserved");
    Ok(())
}

fn default_prefix(tx: &Transaction<'_>, name: &str) -> Result<String> {
    let parts: Vec<_> = name
        .split(|c: char| !(c.is_ascii_alphanumeric() || c == '-' || c == '_'))
        .filter(|part| !part.is_empty())
        .collect();
    let base = if parts.is_empty() {
        "project".into()
    } else {
        parts.join("-")
    };
    let mut prefix = base.clone();
    let mut suffix = 2;
    while tx.query_row(
        "SELECT EXISTS(SELECT 1 FROM board_prefixes WHERE prefix=?)",
        [&prefix],
        |r| r.get::<_, bool>(0),
    )? {
        prefix = format!("{base}-{suffix}");
        suffix += 1;
    }
    Ok(prefix)
}

fn ensure_board(tx: &Transaction<'_>, id: &str, name: &str) -> Result<()> {
    let changed = tx
        .query_row(
            "SELECT name != ? OR archived != 0 FROM boards WHERE id=?",
            params![name, id],
            |r| r.get::<_, bool>(0),
        )
        .optional()?;
    if let Some(changed) = changed {
        if changed {
            tx.execute(
                "UPDATE boards SET name=?,archived=0 WHERE id=?",
                params![name, id],
            )?;
        }
        return Ok(());
    }
    let prefix = default_prefix(tx, if id == "root" { "root" } else { name })?;
    reserve_prefix(tx, id, &prefix)?;
    tx.execute(
        "INSERT OR IGNORE INTO board_counters(owner) VALUES(?)",
        [id],
    )?;
    tx.execute(
        "INSERT INTO boards(id,name,prefix) VALUES(?,?,?)",
        params![id, name, prefix],
    )?;
    for (position, title) in ["Backlog", "Ready", "In progress", "Done"]
        .iter()
        .enumerate()
    {
        tx.execute(
            "INSERT INTO columns(id,board,position,name) VALUES(?,?,?,?)",
            params![uuid::Uuid::new_v4().to_string(), id, position, title],
        )?;
    }
    Ok(())
}

pub(crate) fn identities(cfg: &crate::config::Config) -> Vec<(String, String)> {
    cfg.projects
        .iter()
        .map(|p| (p.id.clone(), p.name.clone()))
        .collect()
}

#[cfg(test)]
mod tests;
