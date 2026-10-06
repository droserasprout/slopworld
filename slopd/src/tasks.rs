//! Task data and participant identity rules. service owns policy and accepted indexes;
//! records and the temporary legacy adapter own persistence formats.

use serde::{Deserialize, Serialize};

/// The user at the keyboard, never a session.
/// Host `slopctl` uses this identity. The daemon accepts it only with the root token.
/// A grant cannot use this identity, regardless of its grantor name.
pub const HOST: &str = crate::shared::protocol::HOST_IDENTITY;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Status {
    Queued,
    Accepted,
    Working,
    Done,
    Failed,
    Canceled,
}

crate::wire_enum!(Status, {
    Status::Queued => crate::shared::protocol::enums::task_status::QUEUED,
    Status::Accepted => crate::shared::protocol::enums::task_status::ACCEPTED,
    Status::Working => crate::shared::protocol::enums::task_status::WORKING,
    Status::Done => crate::shared::protocol::enums::task_status::DONE,
    Status::Failed => crate::shared::protocol::enums::task_status::FAILED,
    Status::Canceled => crate::shared::protocol::enums::task_status::CANCELED,
});

impl Status {
    /// Whether the task reached a final state.
    /// `prune` may remove these tasks. An inbox excludes them unless requested.
    pub fn is_terminal(self) -> bool {
        matches!(self, Status::Done | Status::Failed | Status::Canceled)
    }
}

/// Resolved by the manager while the session boundary holds identity stable.
pub(crate) struct Participant {
    pub(crate) name: String,
    pub(crate) identity: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Task {
    pub id: String,
    pub from: String,
    pub to: String,
    /// Absent in legacy records. Never infer authority from a reused display name.
    #[serde(default)]
    pub(crate) from_id: String,
    #[serde(default)]
    pub(crate) to_id: String,
    pub body: String,
    pub status: Status,
    pub note: Option<String>,
    /// Optional OpenRouter summary for the compact sidebar.
    /// The task body remains the source of truth.
    /// Older task files can omit this field.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub summary: Option<String>,
    pub created_ms: u64,
    pub updated_ms: u64,
    /// Present only when a task owns a worker that the daemon started.
    /// The task body remains the source of truth.
    /// The daemon gives the worker this task ID at startup.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub worker: Option<WorkerTask>,
}

impl Task {
    fn visible_to(&self, identity: &str) -> bool {
        Self::owns(&self.from_id, &self.from, identity) || self.recipient_is(identity)
    }

    fn recipient_is(&self, identity: &str) -> bool {
        Self::owns(&self.to_id, &self.to, identity)
    }

    fn owns(recorded: &str, name: &str, identity: &str) -> bool {
        (!recorded.is_empty() && recorded == identity)
            || (recorded.is_empty() && name == HOST && identity == HOST)
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct WorkerTask {
    pub session: String,
    pub parent: String,
    /// Persistent workers remain configured after their process exits. One-shot workers do not.
    #[serde(default)]
    pub durable: bool,
}

mod legacy;
#[cfg(test)]
mod records;
mod service;
pub(crate) use service::{BatchResult, TaskStamp, Tasks};
