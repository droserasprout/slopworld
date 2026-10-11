//! Files HTTP routes. Each child owns its filesystem or process policy and request boundary.
mod browse;
mod git;
mod mutations;
mod preview;
mod search;

pub(crate) use browse::browse;
#[cfg(test)]
pub(crate) use browse::{browse_limit, filter_gitignored, list_dir};
pub(crate) use git::git_status;
pub(crate) use mutations::{create_file, remove_file, rename_file};
#[cfg(test)]
pub(crate) use mutations::{entry_name, file_path};
#[cfg(test)]
pub(crate) use preview::{IMAGE_LIMIT, read_image_bytes, read_preview};
pub(crate) use preview::{READ_LIMIT, READ_LIMIT_BYTES, file_stat, read_file, read_image};
pub(crate) use search::search;
#[cfg(test)]
pub(crate) use search::{SEARCH_TEXT_LIMIT, search_preview};
