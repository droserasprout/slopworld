//! Targeted workspace persistence. Record owners prepare changes; the manager owns
//! validation, operation guards, recovery ordering, and accepted-state publication.
//! Startup selects these owners after explicit legacy migration and validation.

#[cfg(test)]
mod record;
mod session_document;
pub(crate) mod sessions;
pub(crate) mod target;
pub(crate) mod transaction;

#[cfg(test)]
mod tests;

pub(crate) mod workspace;
mod workspace_document;

pub(crate) mod layout;
pub(crate) mod migration;

pub(crate) mod startup;
