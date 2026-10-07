//! Serializable machine settings, independent of workspace records and catalogs.
//! The manager validates an assembled Config before publishing accepted settings.

use super::daemon::*;
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct Settings {
    #[serde(default)]
    pub daemon: Daemon,
    #[serde(default)]
    pub defaults: Defaults,
    /// Host applications used by file actions.
    #[serde(default)]
    pub commands: CommandDefaults,
}

// Root document edits never own workspace membership.
#[path = "settings_document.rs"]
pub(crate) mod document;

#[cfg(test)]
#[path = "settings_tests.rs"]
mod tests;
