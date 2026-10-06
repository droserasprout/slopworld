//! Mutation destinations selected by feature owners, before disk preparation.
//! The legacy adapter maps workspace records to the inline root until cutover.

use super::*;

#[derive(Clone, Copy)]
pub(in crate::session::manager) enum ConfigMutation {
    Agents,
    HostShells,
    Projects,
    ProjectReferences,
    Library,
    #[cfg(test)]
    Fixture,
}

impl ConfigMutation {
    pub(super) fn writes_root(self) -> bool {
        !matches!(self, Self::Library)
    }

    pub(super) fn writes_library(self) -> bool {
        match self {
            Self::Library => true,
            #[cfg(test)]
            Self::Fixture => true,
            _ => false,
        }
    }

    /// Reject an ownership mistake before cache, tmux, or disk effects. Full
    /// cross-store validation still belongs to prepare_candidate and worktrees.
    pub(super) fn validate(self, old: &Config, new: &Config) -> Result<()> {
        let old_root = toml::Value::try_from(old)?;
        let new_root = toml::Value::try_from(new)?;
        let allowed: &[&str] = match self {
            Self::Agents => &["session"],
            Self::HostShells => &["host_terminal"],
            Self::Projects => &["project"],
            Self::ProjectReferences => &["project", "session", "host_terminal"],
            Self::Library => &[],
            #[cfg(test)]
            Self::Fixture => return Ok(()),
        };
        for key in [
            "daemon",
            "defaults",
            "commands",
            "project",
            "session",
            "host_terminal",
        ] {
            if !allowed.contains(&key) && old_root.get(key) != new_root.get(key) {
                bail!("configuration mutation does not own {key}");
            }
        }
        if !self.writes_library()
            && serde_json::to_value(&old.library)? != serde_json::to_value(&new.library)?
        {
            bail!("configuration mutation does not own library");
        }
        Ok(())
    }
}
