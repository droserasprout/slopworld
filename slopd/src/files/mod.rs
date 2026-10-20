//! Filesystem and external-tool services. HTTP decoding and wire projection belong to api.
//! Preview scope and descriptor limits stay together; Git inspection belongs to crate::git.
pub(crate) mod browse;
pub(crate) mod highlighting;
pub(crate) mod mutations;
pub(crate) mod preview;
mod preview_scope;
pub(crate) mod search;

pub(crate) mod actions;
pub(crate) mod desktop;
