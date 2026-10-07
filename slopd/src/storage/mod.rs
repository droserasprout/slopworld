//! Targeted workspace persistence. Record owners prepare changes; the manager owns
//! validation, operation guards, recovery ordering, and accepted-state publication.
//! Startup selects these owners after layout validation.

pub(crate) mod document;
mod session_document;
pub(crate) mod sessions;
pub(crate) mod target;
pub(crate) mod transaction;

#[cfg(test)]
mod tests;

pub(crate) mod workspace;
mod workspace_document;

pub(crate) mod layout;

pub(crate) mod startup;
