//! Catalog-backed checkout moves. Callers retain their operation guards through commit or rollback.
//! Pending records retain both paths across daemon termination; recovery never guesses which tree to delete.

use super::{relocate_tree, Store, Worktree};
use anyhow::{anyhow, Result};
use std::path::Path;

pub(crate) struct Relocations {
    original: Store,
    candidate: Store,
    moves: Vec<(usize, Worktree)>,
    completed: usize,
}

impl Relocations {
    pub(crate) fn new(store: Store, moves: Vec<(usize, Worktree)>) -> Self {
        let mut candidate = store.clone();
        for (index, destination) in &moves {
            candidate.worktrees[*index] = destination.clone();
        }
        Self {
            original: store,
            candidate,
            moves,
            completed: 0,
        }
    }

    async fn record_intent(&self, config: &Path) -> Result<()> {
        if self.moves.is_empty() {
            return Ok(());
        }
        let mut pending = self.original.clone();
        for (index, destination) in &self.moves {
            let source = &mut pending.worktrees[*index];
            source.phase = "relocating".into();
            source.error = format!("Interrupted relocation: inspect {} and {} and repair Git registration before editing the catalog.", source.path, destination.path);
        }
        pending.save(config).await
    }

    pub(crate) async fn execute(&mut self, config: &Path) -> Result<()> {
        self.record_intent(config).await?;
        for (index, destination) in &self.moves {
            let source = &self.original.worktrees[*index];
            if let Err(error) = relocate_tree(source, Path::new(&destination.path)).await {
                // The failed move can itself need manual recovery if Git repair and its rollback fail.
                // Restore earlier successful moves but retain intent for the whole batch.
                return match self.restore_completed().await {
                    Ok(()) => Err(error),
                    Err(rollback) => Err(anyhow!("{error:#}; rollback also failed: {rollback:#}")),
                };
            }
            self.completed += 1;
        }
        if !self.moves.is_empty() {
            if let Err(error) = self.candidate.save(config).await {
                return match self.rollback(config).await {
                    Ok(()) => Err(error),
                    Err(rollback) => Err(anyhow!("{error:#}; rollback also failed: {rollback:#}")),
                };
            }
        }
        Ok(())
    }

    async fn restore_completed(&mut self) -> Result<()> {
        while self.completed > 0 {
            let (index, destination) = &self.moves[self.completed - 1];
            relocate_tree(
                destination,
                Path::new(&self.original.worktrees[*index].path),
            )
            .await?;
            self.completed -= 1;
        }
        Ok(())
    }

    pub(crate) async fn rollback(&mut self, config: &Path) -> Result<()> {
        if self.moves.is_empty() {
            return Ok(());
        }
        // A committed candidate may already be on disk. Record reverse recovery intent
        // before moving anything back, so a failed rollback never leaves a ready stale path.
        self.record_intent(config).await?;
        self.restore_completed().await?;
        self.original.save(config).await
    }
}
