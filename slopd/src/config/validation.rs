//! Cross-entry validation for a loaded configuration.

use anyhow::{bail, Result};

use super::Config;

pub(super) fn validate_loaded(cfg: &Config) -> Result<()> {
    for session in &cfg.sessions {
        if session.state_id.trim().is_empty() {
            bail!(
                "session {:?} has no private-state identity; recreate the session entry",
                session.name
            );
        }
    }
    Ok(())
}
