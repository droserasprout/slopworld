//! Files HTTP routes. crate::files owns filesystem and process policy.
mod browse;
mod git;
mod mutations;
mod preview;
mod search;

pub(crate) use browse::browse;
pub(crate) use git::git_status;
pub(crate) use mutations::{create_file, remove_file, rename_file};
pub(crate) use preview::{file_stat, read_file, read_image};
pub(crate) use search::search;
