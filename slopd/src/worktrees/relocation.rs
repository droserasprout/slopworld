//! Catalog-backed checkout moves. Callers retain their operation guards through commit or rollback.
//! Pending records retain both paths across daemon termination; recovery never guesses which tree to delete.

use super::{Store, Worktree, relocate_tree};
use anyhow::{Result, anyhow};
use std::path::Path;

/// Persistence is selected by the manager; Git relocation never chooses a layout.
pub(crate) trait Persistence {
    fn save_worktree_records(
        &self,
        store: &Store,
    ) -> impl std::future::Future<Output = Result<()>> + Send;
}
impl Persistence for Path {
    async fn save_worktree_records(&self, store: &Store) -> Result<()> {
        store.save(self).await
    }
}

pub(crate) struct Relocations {
    original: Store,
    candidate: Store,
    moves: Vec<(usize, Worktree)>,
    completed: usize,
}

impl Relocations {
    pub(crate) fn new(store: Store, moves: Vec<(usize, Worktree)>) -> Result<Self> {
        let mut candidate = store.clone();
        for (index, destination) in &moves {
            let worktree = candidate
                .worktrees
                .get_mut(*index)
                .ok_or_else(|| anyhow!("relocation source disappeared from the catalog"))?;
            *worktree = destination.clone();
        }
        Ok(Self {
            original: store,
            candidate,
            moves,
            completed: 0,
        })
    }

    async fn record_intent(&self, owner: &(impl Persistence + Sync + ?Sized)) -> Result<()> {
        if self.moves.is_empty() {
            return Ok(());
        }
        let mut pending = self.original.clone();
        for (index, destination) in &self.moves {
            let source = pending
                .worktrees
                .get_mut(*index)
                .ok_or_else(|| anyhow!("relocation source disappeared from the catalog"))?;
            source.phase = "relocating".into();
            source.error = format!(
                "Interrupted relocation: inspect {} and {} and repair Git registration before editing the catalog.",
                source.path, destination.path
            );
        }
        owner.save_worktree_records(&pending).await
    }

    pub(crate) async fn execute_pending(
        &mut self,
        owner: &(impl Persistence + Sync + ?Sized),
    ) -> Result<()> {
        self.record_intent(owner).await?;
        for (index, destination) in &self.moves {
            let source = self
                .original
                .worktrees
                .get(*index)
                .ok_or_else(|| anyhow!("relocation source disappeared from the catalog"))?;
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
        Ok(())
    }

    pub(crate) fn has_moves(&self) -> bool {
        !self.moves.is_empty()
    }

    pub(crate) fn candidate(&self) -> &Store {
        &self.candidate
    }

    pub(crate) async fn execute(
        &mut self,
        owner: &(impl Persistence + Sync + ?Sized),
    ) -> Result<()> {
        self.execute_pending(owner).await?;
        if !self.moves.is_empty()
            && let Err(error) = owner.save_worktree_records(&self.candidate).await
        {
            return match self.rollback(owner).await {
                Ok(()) => Err(error),
                Err(rollback) => Err(anyhow!("{error:#}; rollback also failed: {rollback:#}")),
            };
        }
        Ok(())
    }

    async fn restore_completed(&mut self) -> Result<()> {
        while self.completed > 0 {
            let Some((index, destination)) = self.moves.get(self.completed - 1) else {
                return Err(anyhow!("completed relocation is missing from its plan"));
            };
            let source = self
                .original
                .worktrees
                .get(*index)
                .ok_or_else(|| anyhow!("relocation source disappeared from the catalog"))?;
            relocate_tree(destination, Path::new(&source.path)).await?;
            self.completed -= 1;
        }
        Ok(())
    }

    pub(crate) async fn rollback(
        &mut self,
        owner: &(impl Persistence + Sync + ?Sized),
    ) -> Result<()> {
        if self.moves.is_empty() {
            return Ok(());
        }
        // A committed candidate may already be on disk. Record reverse recovery intent
        // before moving anything back, so a failed rollback never leaves a ready stale path.
        self.record_intent(owner).await?;
        self.restore_completed().await?;
        owner.save_worktree_records(&self.original).await
    }
}
