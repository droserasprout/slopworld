//! SQLite owns board membership, ordering and card metadata. Accepted project IDs
//! supply board liveness. No board operation controls a session or a worktree.
mod edits;
mod snapshot;
use crate::shared::wire;
use anyhow::{Context, Result};
use rusqlite::{Connection, Transaction, TransactionBehavior, params};
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

    /// Lifecycle callers only need the identities still reserved by this store.
    pub(crate) async fn sync_projects(
        &self,
        projects: Vec<(String, String)>,
    ) -> Result<Vec<String>> {
        self.transaction(projects, |tx| {
            Ok(tx
                .prepare("SELECT id FROM boards")?
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
            db.execute_batch(include_str!("schema.sql"))?;
            let tx = db.transaction_with_behavior(TransactionBehavior::Immediate)?;
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

fn ensure_board(tx: &Transaction<'_>, id: &str, name: &str) -> Result<()> {
    if tx.execute(
        "INSERT INTO boards(id,name) VALUES(?,?) ON CONFLICT(id) DO NOTHING",
        params![id, name],
    )? == 1
    {
        for (position, title) in ["Backlog", "Ready", "In progress", "Done"]
            .iter()
            .enumerate()
        {
            tx.execute(
                "INSERT INTO columns(id,board,position,name) VALUES(?,?,?,?)",
                params![uuid::Uuid::new_v4().to_string(), id, position, title],
            )?;
        }
    } else {
        tx.execute(
            "UPDATE boards SET name=?,archived=0 WHERE id=? AND (name!=? OR archived!=0)",
            params![name, id, name],
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
