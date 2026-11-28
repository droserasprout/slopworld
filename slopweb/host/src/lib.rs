//! Native slopweb host: launcher owns preparation, fonts owns discovery and delivery,
//! server owns same-origin transport and host-only daemon credentials.
mod archive_identity;
pub mod fonts;
pub mod launcher;
pub mod server;
