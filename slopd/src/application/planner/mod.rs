//! Manual todo.txt backlog persistence and worker-status projection.
//! Store owns document revisions; Tasks own linked worker progress.
mod store;
pub(crate) use store::Store;
pub(crate) use store::{LinkKind, LinkReference};
