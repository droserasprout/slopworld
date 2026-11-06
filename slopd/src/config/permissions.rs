//! Current agent authority. Credentials identify callers; this policy selects their targets.
use serde::{Deserialize, Serialize};

#[derive(Debug, Default, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum PermissionScope {
    #[default]
    Deny,
    OwnWorkers,
    ProjectAgents,
}

impl PermissionScope {
    pub(crate) fn allows(self, own_worker: bool, same_project: bool) -> bool {
        match self {
            Self::Deny => false,
            Self::OwnWorkers => own_worker,
            Self::ProjectAgents => same_project,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(default, deny_unknown_fields)]
pub struct AgentPermissions {
    pub followup: PermissionScope,
    pub cancel: PermissionScope,
    pub terminal: PermissionScope,
    pub lifecycle: PermissionScope,
    pub delete: PermissionScope,
    pub worktrees: bool,
}

impl Default for AgentPermissions {
    fn default() -> Self {
        Self {
            followup: PermissionScope::OwnWorkers,
            cancel: PermissionScope::OwnWorkers,
            terminal: PermissionScope::Deny,
            lifecycle: PermissionScope::Deny,
            delete: PermissionScope::Deny,
            worktrees: false,
        }
    }
}

impl AgentPermissions {
    pub(crate) fn intersect(self, ceiling: Self) -> Self {
        Self {
            followup: self.followup.min(ceiling.followup),
            cancel: self.cancel.min(ceiling.cancel),
            terminal: self.terminal.min(ceiling.terminal),
            lifecycle: self.lifecycle.min(ceiling.lifecycle),
            delete: self.delete.min(ceiling.delete),
            worktrees: self.worktrees && ceiling.worktrees,
        }
    }
}

#[cfg(test)]
impl From<super::WorkerAccess> for AgentPermissions {
    fn from(access: super::WorkerAccess) -> Self {
        let scope = |enabled| {
            if enabled {
                PermissionScope::OwnWorkers
            } else {
                PermissionScope::Deny
            }
        };
        Self {
            followup: scope(access.followup),
            cancel: scope(access.cancel),
            terminal: scope(access.terminal),
            lifecycle: scope(access.lifecycle),
            ..Self::default()
        }
    }
}
