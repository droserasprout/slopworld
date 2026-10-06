//! Targeted workspace persistence. Record owners prepare changes; the manager owns
//! validation, operation guards, recovery ordering, and accepted-state publication.
//! This backend remains unselected until the complete offline migration is ready.

mod record;
mod session_document;
pub(crate) mod sessions;
pub(crate) mod target;
pub(crate) mod transaction;

#[cfg(test)]
mod tests;

pub(crate) mod workspace;
mod workspace_document;
