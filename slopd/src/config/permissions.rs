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

#[derive(Debug, Default, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum BoardAccess {
    Off,
    Read,
    #[default]
    Write,
    Admin,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(default, deny_unknown_fields)]
pub struct AgentPermissions {
    pub followup: PermissionScope,
    pub cancel: PermissionScope,
    pub terminal: PermissionScope,
    pub lifecycle: PermissionScope,
    pub delete: PermissionScope,
    pub board_access: BoardAccess,
    pub own_project_board: bool,
    pub visible_boards: std::collections::BTreeSet<String>,
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
            board_access: BoardAccess::Write,
            own_project_board: true,
            visible_boards: Default::default(),
            worktrees: false,
        }
    }
}

impl AgentPermissions {
    pub(crate) fn sees_board(&self, board: &str, project: Option<&str>) -> bool {
        self.visible_boards.contains(board) || (self.own_project_board && project == Some(board))
    }

    pub(crate) fn intersect(self, ceiling: &Self, project: &str) -> Self {
        // Scoped spawns stay in the parent project. An explicit grant on one side
        // can overlap the dynamic project checkbox on the other side.
        let visible_boards = self
            .visible_boards
            .union(&ceiling.visible_boards)
            .filter(|id| {
                self.sees_board(id, Some(project)) && ceiling.sees_board(id, Some(project))
            })
            .cloned()
            .collect();
        Self {
            followup: self.followup.min(ceiling.followup),
            cancel: self.cancel.min(ceiling.cancel),
            terminal: self.terminal.min(ceiling.terminal),
            lifecycle: self.lifecycle.min(ceiling.lifecycle),
            delete: self.delete.min(ceiling.delete),
            board_access: self.board_access.min(ceiling.board_access),
            own_project_board: self.own_project_board && ceiling.own_project_board,
            visible_boards,
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

#[cfg(test)]
#[path = "permissions_tests.rs"]
mod tests;
